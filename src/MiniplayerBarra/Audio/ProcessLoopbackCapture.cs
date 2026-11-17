using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using NAudio.Wasapi.CoreAudioApi.Interfaces;
using NAudio.Wave;

namespace MiniplayerBarra.Audio;

/// <summary>
/// Captura só o som de uma árvore de processos (Windows 10 2004+), via <c>ActivateAudioInterfaceAsync</c>
/// com <c>VAD\Process_Loopback</c>. O NAudio 2.4 não expõe essa ativação, então ela é feita por P/Invoke
/// e o <see cref="IAudioClient"/> resultante vai para o <see cref="AudioClient"/> do NAudio.
/// Tudo (ativação, inicialização, leitura) roda numa thread própria; <see cref="Start"/> só cria a thread.
/// </summary>
internal sealed class ProcessLoopbackCapture : IAudioCapture
{
    private const string VirtualDevice = @"VAD\Process_Loopback";
    private const int ActivationTypeProcessLoopback = 1;
    private const int IncludeTargetProcessTree = 0;
    private const ushort VtBlob = 65;
    private const long BufferDuration100ns = 2_000_000;   // 200 ms
    private static readonly TimeSpan ActivationTimeout = TimeSpan.FromSeconds(5);

    // No process loopback, GetMixFormat não funciona: o formato tem que ser passado.
    private static readonly WaveFormat Format = new(44100, 16, 2);

    private readonly int _pid;
    private readonly AutoResetEvent _wake = new(false);
    private readonly MonoConverter _converter = new(Format);
    private byte[] _bytes = Array.Empty<byte>();
    private Thread? _thread;
    private volatile bool _stop;
    private bool _disposed;

    public ProcessLoopbackCapture(int pid) => _pid = pid;

    public int SampleRate => Format.SampleRate;
    public event MonoDataHandler? Data;
    public event Action<Exception, bool>? Failed;

    public void Start()
    {
        if (_thread != null) return;
        _thread = new Thread(Run) { IsBackground = true, Name = "Ondas: captura por processo" };
        _thread.SetApartmentState(ApartmentState.MTA);
        _thread.Start();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _stop = true;
        _wake.Set();
        bool finished = _thread is null || (_thread != Thread.CurrentThread && _thread.Join(TimeSpan.FromSeconds(2)));
        // O evento está registrado no cliente de áudio; só é liberado depois que a thread terminou.
        if (finished) _wake.Dispose();
    }

    private void Run()
    {
        AudioClient? client = null;
        bool running = false;
        try
        {
            var iac = Activate();
            if (iac is null) return;   // pararam durante a ativação
            client = new AudioClient(iac);
            client.Initialize(AudioClientShareMode.Shared,
                AudioClientStreamFlags.Loopback | AudioClientStreamFlags.EventCallback | AudioClientStreamFlags.AutoConvertPcm,
                BufferDuration100ns, 0, Format, Guid.Empty);
            client.SetEventHandle(_wake.SafeWaitHandle.DangerousGetHandle());
            var capture = client.AudioCaptureClient;
            client.Start();
            running = true;

            while (!_stop)
            {
                _wake.WaitOne(100);
                int packet;
                while (!_stop && (packet = capture.GetNextPacketSize()) > 0)
                {
                    IntPtr data = capture.GetBuffer(out int frames, out AudioClientBufferFlags flags);
                    ReadOnlySpan<float> mono;
                    if ((flags & AudioClientBufferFlags.Silent) != 0) mono = _converter.Zeros(frames);
                    else
                    {
                        int byteCount = frames * Format.BlockAlign;
                        if (_bytes.Length < byteCount) _bytes = new byte[byteCount];
                        Marshal.Copy(data, _bytes, 0, byteCount);
                        mono = _converter.Convert(_bytes.AsSpan(0, byteCount));
                    }
                    capture.ReleaseBuffer(frames);
                    Data?.Invoke(mono);
                }
            }
        }
        catch (Exception ex)
        {
            if (!_stop) Failed?.Invoke(ex, running);
        }
        finally
        {
            try { if (running) client!.Stop(); } catch (Exception) { }
            client?.Dispose();
        }
    }

    /// <summary>Ativa o IAudioClient de process loopback; null se pediram para parar antes de terminar.</summary>
    private IAudioClient? Activate()
    {
        var parameters = new AudioClientActivationParams
        {
            ActivationType = ActivationTypeProcessLoopback,
            TargetProcessId = (uint)_pid,
            ProcessLoopbackMode = IncludeTargetProcessTree,
        };
        int paramsSize = Marshal.SizeOf<AudioClientActivationParams>();
        IntPtr pParams = Marshal.AllocHGlobal(paramsSize);
        IntPtr pVariant = Marshal.AllocHGlobal(Marshal.SizeOf<PropVariantBlob>());
        try
        {
            Marshal.StructureToPtr(parameters, pParams, false);
            Marshal.StructureToPtr(new PropVariantBlob { vt = VtBlob, cbSize = (uint)paramsSize, pBlobData = pParams }, pVariant, false);

            var handler = new CompletionHandler();
            ActivateAudioInterfaceAsync(VirtualDevice, typeof(IAudioClient).GUID, pVariant, handler, out _);

            var deadline = DateTime.UtcNow + ActivationTimeout;
            while (!handler.Result.Task.Wait(50))
            {
                if (_stop) return null;
                if (DateTime.UtcNow > deadline) throw new TimeoutException("A ativação da captura por processo não respondeu.");
            }
            return handler.Result.Task.GetAwaiter().GetResult();
        }
        finally
        {
            Marshal.FreeHGlobal(pVariant);
            Marshal.FreeHGlobal(pParams);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AudioClientActivationParams
    {
        public int ActivationType;
        public uint TargetProcessId;
        public int ProcessLoopbackMode;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct PropVariantBlob
    {
        [FieldOffset(0)] public ushort vt;
        [FieldOffset(8)] public uint cbSize;
        [FieldOffset(16)] public IntPtr pBlobData;
    }

    [DllImport("Mmdevapi.dll", ExactSpelling = true, PreserveSig = false)]
    private static extern void ActivateAudioInterfaceAsync(
        [MarshalAs(UnmanagedType.LPWStr)] string deviceInterfacePath,
        [MarshalAs(UnmanagedType.LPStruct)] Guid riid,
        IntPtr activationParams,
        IActivateAudioInterfaceCompletionHandler completionHandler,
        out IActivateAudioInterfaceAsyncOperation activationOperation);

    [ComImport, Guid("94ea2b94-e9cc-49e0-c0ff-ee64ca8f5b90"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAgileObject { }

    /// <summary>Chamado numa thread do sistema; IAgileObject evita que o COM tente marshaling.</summary>
    private sealed class CompletionHandler : IActivateAudioInterfaceCompletionHandler, IAgileObject
    {
        public readonly TaskCompletionSource<IAudioClient> Result = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void ActivateCompleted(IActivateAudioInterfaceAsyncOperation operation)
        {
            try
            {
                operation.GetActivateResult(out int hr, out object unknown);
                if (hr != 0) Result.TrySetException(Marshal.GetExceptionForHR(hr)!);
                else Result.TrySetResult((IAudioClient)unknown);
            }
            catch (Exception ex) { Result.TrySetException(ex); }
        }
    }
}
