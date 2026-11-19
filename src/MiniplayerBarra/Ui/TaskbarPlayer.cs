using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using MiniplayerBarra.Core;
using Microsoft.Win32;

namespace MiniplayerBarra.Ui;

/// <summary>
/// Player encaixado na barra de tarefas: uma janela filha (em camadas, fundo transparente) da
/// Shell_TrayWnd, no lugar dado por TaskbarProbe + TaskbarSlot. Quando o Explorer reinicia, a
/// janela filha morre junto; uma janela oculta de nível superior escuta "TaskbarCreated" e
/// reencaixa. A cada quadro (SetBands) só redesenha o bitmap e chama UpdateLayeredWindow.
/// </summary>
internal sealed class TaskbarPlayer : IPlayerView
{
    private const int MarginPx = 8;
    private const int CheckIntervalMs = 3000;

    private readonly Listener _listener;
    private readonly System.Windows.Forms.Timer _check;
    private readonly System.Windows.Forms.Timer _volumeTimer;
    private readonly Bitmap _measureBmp = new(1, 1);
    private EmbeddedForm? _form;
    private Theme _theme = Theme.Current();
    private PlayerState? _state;
    private float[] _bands = Array.Empty<float>();
    private TaskbarInfo? _info;
    private PlayerLayout? _layout;
    private float _scale = 1;
    private Rectangle _placed;     // posição atual na tela
    private bool _shown;
    private Surface? _surface;
    private string? _volumeText;   // "Volume N%" no lugar do artista, por 1,5 s
    private bool _disposed;

    public event Action<PlayerHit>? Clicked;
    public event Action? PauseAllRequested;
    public event Action? ExitRequested;
    public event Action<int>? VolumeWheel;

    private TaskbarPlayer()
    {
        _listener = new Listener(this);
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
        // Confere a cada 3 s se algo mudou na barra (alinhamento, Widgets, Barra de Bateria,
        // largura dos ícones). Só roda com o player visível.
        _check = new System.Windows.Forms.Timer { Interval = CheckIntervalMs };
        _check.Tick += (_, _) => CheckTaskbar();
        _volumeTimer = new System.Windows.Forms.Timer { Interval = 1500 };
        _volumeTimer.Tick += (_, _) =>
        {
            _volumeTimer.Stop();
            _volumeText = null;
            if (_disposed) return;
            Relayout();
            Redraw();
        };
    }

    /// <summary>Retorna null se não conseguir encaixar.</summary>
    public static TaskbarPlayer? TryCreate()
    {
        if (FindTray() == IntPtr.Zero) return null;
        var player = new TaskbarPlayer();
        if (!player.Attach())
        {
            player.Dispose();
            return null;
        }
        return player;
    }

    public void Show(PlayerState? state)
    {
        if (_disposed) return;
        var previous = _state;
        _state = state;
        if (state is null)
        {
            _check.Stop();
            _shown = false;
            if (_form is { IsAlive: true }) Native.ShowWindow(_form.Handle, Native.SW_HIDE);
            _form?.ResetHover();
            return;
        }

        if (_form is null || !_form.IsAlive)
        {
            Attach(); // reposiciona e desenha
            return;
        }

        if (!_shown)
        {
            // Estava escondido: a barra pode ter mudado enquanto o timer estava parado.
            Replace();
        }
        else if (previous is null || previous.Media.Title != state.Media.Title || previous.Media.Artist != state.Media.Artist)
        {
            Relayout();
        }
        Redraw();
    }

    public void ShowVolume(int percent)
    {
        if (_disposed || _state is null) return;
        _volumeText = $"Volume {percent}%";
        _volumeTimer.Stop();
        _volumeTimer.Start();
        Relayout();
        Redraw();
    }

    public void SetBands(float[] bands)
    {
        if (_disposed || bands is null) return;
        if (_bands.Length != bands.Length) _bands = new float[bands.Length];
        Array.Copy(bands, _bands, bands.Length);
        if (_shown) Redraw();
    }

    private static IntPtr FindTray() => Native.FindWindow("Shell_TrayWnd", null);

    private bool Attach()
    {
        try
        {
            _form?.Dispose();
            _form = null;
            _shown = false;
            var tray = FindTray();
            if (tray == IntPtr.Zero) return false;

            var form = new EmbeddedForm(this);
            var handle = form.Handle;
            int style = (Native.GetWindowLong(handle, Native.GWL_STYLE) & ~Native.WS_POPUP) | Native.WS_CHILD | Native.WS_CLIPSIBLINGS;
            Native.SetWindowLong(handle, Native.GWL_STYLE, style);
            if (Native.SetParent(handle, tray) == IntPtr.Zero)
            {
                form.Dispose();
                return false;
            }
            _form = form;
            _placed = Rectangle.Empty;
            if (_state is not null)
            {
                Replace();
                Redraw();
            }
            return true;
        }
        catch (Exception ex)
        {
            Log.Error("Player encaixe", ex);
            return false;
        }
    }

    private void CheckTaskbar()
    {
        try
        {
            if (_state is null) return;
            if (_form is null || !_form.IsAlive)
            {
                Attach();
                return;
            }
            var tray = FindTray();
            var info = TaskbarProbe.Read(tray);
            if (info is null || info == _info) return;
            _info = info;
            _scale = ScaleOf(tray);
            Relayout();
            Redraw();
        }
        catch (Exception ex)
        {
            Log.Error("Player checagem", ex);
        }
    }

    /// <summary>Lê a barra de novo (UIA) e recalcula posição e tamanho.</summary>
    internal void Replace()
    {
        try
        {
            var tray = FindTray();
            _info = TaskbarProbe.Read(tray) ?? _info;
            _scale = ScaleOf(tray);
            Relayout();
        }
        catch (Exception ex)
        {
            Log.Error("Player posição", ex);
        }
    }

    private static float ScaleOf(IntPtr tray)
    {
        float s = tray == IntPtr.Zero ? 1 : Native.GetDpiForWindow(tray) / 96f;
        return s > 0 ? s : 1;
    }

    private void Relayout()
    {
        var form = _form;
        var state = _state;
        var info = _info;
        if (form is null || !form.IsAlive || state is null || info is null) return;

        string title = state.Media.Title ?? "";
        string artist = _volumeText ?? state.Media.Artist ?? "";
        using var g = Graphics.FromImage(_measureBmp);
        var desired = PlayerLayout.Compute(g, title, artist, _scale, int.MaxValue / 2);
        int margin = (int)Math.Round(MarginPx * _scale);
        var slot = TaskbarSlot.Place(info, desired.Size.Width, PlayerLayout.MinWidth(_scale), desired.Size.Height, margin);
        var layout = slot.Width < desired.Size.Width
            ? PlayerLayout.Compute(g, title, artist, _scale, slot.Width)
            : desired;
        // Barra à esquerda: o player encosta pela direita; se o layout ficou menor que o espaço, ajusta.
        var rect = new Rectangle(info.Centered ? slot.Left : slot.Right - layout.Size.Width, slot.Top,
            layout.Size.Width, layout.Size.Height);
        _layout = layout;
        form.CurrentLayout = layout;

        if (rect != _placed || !_shown)
        {
            Native.GetWindowRect(FindTray(), out var tr);
            Native.SetWindowPos(form.Handle, Native.HWND_TOP, rect.Left - tr.Left, rect.Top - tr.Top,
                rect.Width, rect.Height, Native.SWP_NOACTIVATE | Native.SWP_SHOWWINDOW);
            _placed = rect;
            _shown = true;
            _check.Start();
        }
    }

    private void OnUserPreferenceChanged(object? sender, UserPreferenceChangedEventArgs e)
    {
        _theme = Theme.Current();
        if (_shown) Redraw();
    }

    internal void Redraw()
    {
        try
        {
            var form = _form;
            var state = _state;
            var layout = _layout;
            if (form is null || !form.IsAlive || state is null || layout is null || !_shown) return;

            var size = layout.Size;
            if (_surface is null || _surface.Size != size)
            {
                _surface?.Dispose();
                _surface = new Surface(size);
            }
            var g = _surface.Graphics;
            g.CompositingMode = CompositingMode.SourceCopy;
            // Alfa 1 em vez de 0: invisível, mas a janela continua recebendo o mouse.
            g.Clear(Color.FromArgb(1, 0, 0, 0));
            g.CompositingMode = CompositingMode.SourceOver;
            var drawn = _volumeText is null ? state : state with { Media = state.Media with { Artist = _volumeText } };
            PlayerRenderer.Draw(g, layout, drawn, _bands, _theme, _scale);
            g.Flush(FlushIntention.Sync);
            _surface.Apply(form.Handle);
        }
        catch (Exception ex)
        {
            Log.Error("Player desenho", ex);
        }
    }

    internal PlayerHit OnClick(Point p)
    {
        var hit = _layout?.HitTest(p) ?? PlayerHit.None;
        if (hit != PlayerHit.None) Clicked?.Invoke(hit);
        return hit;
    }

    internal string? TooltipAt(Point p)
    {
        if (_layout is null || _state is null || _layout.HitTest(p) != PlayerHit.OpenSource) return null;
        var m = _state.Media;
        if (string.IsNullOrEmpty(m.Title) && string.IsNullOrEmpty(m.Artist)) return null;
        var text = string.IsNullOrEmpty(m.Artist) ? m.Title : string.IsNullOrEmpty(m.Title) ? m.Artist : $"{m.Title} — {m.Artist}";
        return text + "\nClique: abrir · Rodinha: volume";
    }

    internal void OnWheel(int notches) => VolumeWheel?.Invoke(notches);

    internal void OnPauseAllClicked() => PauseAllRequested?.Invoke();
    internal void OnExitClicked() => ExitRequested?.Invoke();

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        _check.Dispose();
        _volumeTimer.Dispose();
        _form?.Dispose();
        _form = null;
        _surface?.Dispose();
        _surface = null;
        _measureBmp.Dispose();
        _listener.DestroyHandle();
    }

    /// <summary>
    /// Bitmap reaproveitado entre quadros: uma DIB section de 32 bits (alfa pré-multiplicado)
    /// já selecionada num DC de memória, com um Bitmap do GDI+ apontando para os mesmos bytes.
    /// Nada é alocado por quadro.
    /// </summary>
    private sealed class Surface : IDisposable
    {
        private readonly IntPtr _memDc;
        private readonly IntPtr _hBitmap;
        private readonly IntPtr _oldBitmap;
        private readonly Bitmap _bitmap;
        public Graphics Graphics { get; }
        public Size Size { get; }

        public Surface(Size size)
        {
            Size = size;
            var bmi = new Native.BITMAPINFOHEADER
            {
                biSize = System.Runtime.InteropServices.Marshal.SizeOf<Native.BITMAPINFOHEADER>(),
                biWidth = size.Width,
                biHeight = -size.Height, // de cima para baixo
                biPlanes = 1,
                biBitCount = 32,
            };
            var screenDc = Native.GetDC(IntPtr.Zero);
            try
            {
                _memDc = Native.CreateCompatibleDC(screenDc);
                _hBitmap = Native.CreateDIBSection(screenDc, ref bmi, Native.DIB_RGB_COLORS, out var bits, IntPtr.Zero, 0);
                if (_hBitmap == IntPtr.Zero)
                {
                    Native.DeleteDC(_memDc);
                    throw new InvalidOperationException("CreateDIBSection falhou");
                }
                _oldBitmap = Native.SelectObject(_memDc, _hBitmap);
                _bitmap = new Bitmap(size.Width, size.Height, size.Width * 4, PixelFormat.Format32bppPArgb, bits);
                Graphics = Graphics.FromImage(_bitmap);
            }
            finally
            {
                Native.ReleaseDC(IntPtr.Zero, screenDc);
            }
        }

        public void Apply(IntPtr hwnd)
        {
            var size = new Native.SIZE(Size.Width, Size.Height);
            var src = new Native.POINT(0, 0);
            var blend = new Native.BLENDFUNCTION
            {
                BlendOp = Native.AC_SRC_OVER, SourceConstantAlpha = 255, AlphaFormat = Native.AC_SRC_ALPHA,
            };
            if (!Native.UpdateLayeredWindow(hwnd, IntPtr.Zero, IntPtr.Zero, ref size, _memDc, ref src, 0, ref blend, Native.ULW_ALPHA))
                Log.Info($"UpdateLayeredWindow falhou: {System.Runtime.InteropServices.Marshal.GetLastWin32Error()}");
        }

        public void Dispose()
        {
            Graphics.Dispose();
            _bitmap.Dispose();
            Native.SelectObject(_memDc, _oldBitmap);
            Native.DeleteObject(_hBitmap);
            Native.DeleteDC(_memDc);
        }
    }

    /// <summary>Janela oculta de nível superior: recebe TaskbarCreated e mudanças de tela/configuração.</summary>
    private sealed class Listener : NativeWindow
    {
        private static readonly int TaskbarCreated = (int)Native.RegisterWindowMessage("TaskbarCreated");
        private readonly TaskbarPlayer _owner;

        public Listener(TaskbarPlayer owner)
        {
            _owner = owner;
            CreateHandle(new CreateParams { Caption = "MiniplayerBarraListener" });
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == TaskbarCreated)
            {
                Log.Info("Barra de tarefas recriada (Explorer reiniciou): reencaixando");
                _owner.Attach();
            }
            else if (m.Msg is Native.WM_DISPLAYCHANGE or Native.WM_DPICHANGED or Native.WM_SETTINGCHANGE)
            {
                if (_owner._state is not null && _owner._form is { IsAlive: true })
                {
                    _owner.Replace();
                    _owner.Redraw();
                }
            }
            base.WndProc(ref m);
        }
    }

    /// <summary>A janela em si. Pintura via UpdateLayeredWindow; aqui só mouse, tooltip e menu.</summary>
    private sealed class EmbeddedForm : Form
    {
        private readonly TaskbarPlayer _owner;
        private readonly ToolTip _toolTip = new() { ShowAlways = true };
        private readonly ContextMenuStrip _menu = new();
        private readonly System.Windows.Forms.Timer _hoverTimer = new() { Interval = 400 };
        private readonly FocusKeeper _focus = new();
        private bool _exitPicked;
        private string? _hoverText;
        private Point _hoverPoint;
        private bool _destroyed;
        private int _wheelAcc;

        public EmbeddedForm(TaskbarPlayer owner)
        {
            _owner = owner;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            _menu.Items.Add("Pausar tudo", null, (_, _) => _owner.OnPauseAllClicked());
            var exit = _menu.Items.Add("Sair", null, (_, _) => _owner.OnExitClicked());
            // ItemClicked vem antes de Closed: ao fechar o menu, devolve o foco, menos se for "Sair".
            _menu.ItemClicked += (_, e) => _exitPicked = e.ClickedItem == exit;
            _menu.Closed += (_, _) =>
            {
                if (!_exitPicked) _focus.RestoreSoon();
                _exitPicked = false;
            };
            _hoverTimer.Tick += (_, _) => ShowHoverTip();
        }

        public PlayerLayout? CurrentLayout { get; set; }

        public bool IsAlive => !_destroyed && IsHandleCreated && Native.IsWindow(Handle);

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= Native.WS_EX_LAYERED | Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE;
                return cp;
            }
        }

        protected override bool ShowWithoutActivation => true;

        public void ResetHover()
        {
            _hoverText = null;
            _hoverTimer.Stop();
            _toolTip.Hide(this);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            // O Explorer pode pôr a barra na frente já no botão pressionado: guarda antes, ao passar o mouse.
            _focus.Hover();
            var text = _owner.TooltipAt(e.Location);
            if (text == _hoverText) return;
            _hoverText = text;
            _hoverPoint = e.Location;
            _toolTip.Hide(this);
            _hoverTimer.Stop();
            if (text is not null) _hoverTimer.Start();
        }

        // O ToolTip automático não dispara numa janela filha de outro processo:
        // mostramos manualmente depois de 400 ms parado sobre a capa ou o texto, acima da barra.
        private void ShowHoverTip()
        {
            _hoverTimer.Stop();
            if (_hoverText is null) return;
            int x = CurrentLayout?.Cover.Left ?? _hoverPoint.X;
            _toolTip.Show(_hoverText, this, x, -(int)(28 * DeviceDpi / 96f), 5000);
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            // Touchpad manda deltas pequenos: acumula até um clique inteiro (120).
            _wheelAcc += e.Delta;
            int notches = _wheelAcc / 120;
            if (notches == 0) return;
            _wheelAcc -= notches * 120;
            _owner.OnWheel(notches);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _wheelAcc = 0;
            _focus.Leave();
            ResetHover();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button is MouseButtons.Left or MouseButtons.Right) _focus.Press();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button == MouseButtons.Left)
            {
                // Capa/texto trazem a janela da fonte para a frente de propósito: não devolve o foco.
                if (_owner.OnClick(e.Location) != PlayerHit.OpenSource) _focus.RestoreSoon();
                else _focus.Forget();
            }
            else if (e.Button == MouseButtons.Right)
            {
                ResetHover();
                _menu.Show(Cursor.Position);
                // O menu (janela de nível superior nossa) vai para a frente: assim o Esc funciona e
                // ele não fecha sozinho se o Windows devolver o foco a outra janela. Ao fechar,
                // _focus devolve o foco à janela de antes.
                Native.SetForegroundWindow(_menu.Handle);
            }
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == Native.WM_MOUSEACTIVATE)
            {
                m.Result = (IntPtr)Native.MA_NOACTIVATE;
                return;
            }
            base.WndProc(ref m);
        }

        protected override void OnHandleDestroyed(EventArgs e)
        {
            _destroyed = true;
            base.OnHandleDestroyed(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _hoverTimer.Dispose();
                _focus.Dispose();
                _toolTip.Dispose();
                _menu.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
