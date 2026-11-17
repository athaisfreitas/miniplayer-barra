using MiniplayerBarra.Audio;
using MiniplayerBarra.Core;
using MiniplayerBarra.Media;
using MiniplayerBarra.Ui;

namespace MiniplayerBarra;

internal static class Program
{
    private const int RetryIntervalMs = 10_000;

    internal static string DataDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MiniplayerBarra");

    [STAThread]
    private static void Main()
    {
        Log.FilePath = Path.Combine(DataDir, "log.txt");

        using var instance = SingleInstance.TryAcquire("MiniplayerBarra");
        if (instance is null) return; // já está rodando

        // Exceções em handlers de eventos da interface: registra e segue rodando.
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => Log.Error("Interface", e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex) Log.Error("Não tratada", ex);
            else Log.Info($"ERRO [Não tratada] {e.ExceptionObject}");
        };
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Log.Error("Tarefa", e.Exception);
            e.SetObserved();
        };

        ApplicationConfiguration.Initialize();
        // Garante o contexto da interface antes de qualquer await (o controlador o captura).
        SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());

        var exeDir = Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory;
        var settings = Settings.Load(Path.Combine(exeDir, "config.json"));

        try
        {
            if (Environment.ProcessPath is { } exe &&
                StartupRegistration.ShouldRegister(exe, Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)))
                StartupRegistration.Ensure(exe);
        }
        catch (Exception ex)
        {
            Log.Error("Inicialização com o Windows", ex);
        }

        using var app = new Startup(settings);
        app.Begin();
        Application.Run();
    }

    /// <summary>
    /// Monta o app quando a barra de tarefas e o GSMTC estiverem prontos. Se um deles falhar,
    /// registra e tenta de novo a cada 10 s (sem sair).
    /// </summary>
    private sealed class Startup : IDisposable
    {
        private readonly Settings _settings;
        private readonly System.Windows.Forms.Timer _retry = new() { Interval = RetryIntervalMs };
        private readonly AudioMeter _meter = new();
        private TaskbarPlayer? _view;
        private IMediaSessions? _sessions;
        private AppController? _controller;
        private bool _creatingSessions;
        private bool _disposed;

        public Startup(Settings settings)
        {
            _settings = settings;
            _retry.Tick += (_, _) => Attempt();
        }

        public void Begin() => Attempt();

        private void Attempt()
        {
            if (_disposed || _controller is not null) return;

            if (_view is null)
            {
                try
                {
                    _view = TaskbarPlayer.TryCreate();
                }
                catch (Exception ex)
                {
                    Log.Error("Encaixe na barra", ex);
                }
                if (_view is null) Log.Info("Não foi possível encaixar na barra de tarefas; nova tentativa em 10 s");
                else _view.ExitRequested += Application.Exit;
            }

            if (_sessions is null && !_creatingSessions) _ = CreateSessionsAsync();

            TryFinish();
        }

        private async Task CreateSessionsAsync()
        {
            _creatingSessions = true;
            try
            {
                _sessions = await GsmtcMediaSessions.CreateAsync(_settings);
            }
            catch (Exception ex)
            {
                Log.Error("Sessões de mídia (GSMTC)", ex);
                Log.Info("Nova tentativa do GSMTC em 10 s");
            }
            finally
            {
                _creatingSessions = false;
            }
            if (_disposed)
            {
                _sessions?.Dispose();
                _sessions = null;
                return;
            }
            TryFinish();
        }

        private void TryFinish()
        {
            if (_disposed || _controller is not null) return;
            if (_view is null || _sessions is null)
            {
                _retry.Start();
                return;
            }
            _retry.Stop();
            _controller = new AppController(_sessions, _view, _settings, () => Theme.Current().Accent, _meter);
            Log.Info($"Iniciado (ondas {(_settings.Waves ? "ligadas" : "desligadas")})");
            _ = StartControllerAsync(_controller);
        }

        private static async Task StartControllerAsync(AppController controller)
        {
            try
            {
                await controller.StartAsync();
            }
            catch (Exception ex)
            {
                Log.Error("Início do controlador", ex);
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _retry.Dispose();
            _controller?.Dispose();
            _meter.Dispose();
            _view?.Dispose();
            _sessions?.Dispose();
        }
    }
}
