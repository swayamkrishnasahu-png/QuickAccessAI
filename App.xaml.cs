using System.Windows;
using System.Linq;
using QuickAccessAI.Config;
using QuickAccessAI.Services;
using QuickAccessAI.UI;
using Forms = System.Windows.Forms;

namespace QuickAccessAI;

// Base class (System.Windows.Application) is declared by the XAML-generated
// partial; omitted here to avoid the WinForms/WPF 'Application' ambiguity.
public partial class App
{
    private Settings _settings = null!;
    private HotkeyService _hotkey = null!;
    private OverlayWindow _overlay = null!;
    private LlmService _llm = null!;
    private Forms.NotifyIcon _tray = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _settings = Settings.Load();

        // First run: let the user choose a backend (Free Cloud vs Local Ollama).
        // Cancelling the setup screen quits the app.
        if (!_settings.SetupComplete)
        {
            var setup = new SetupWindow(_settings);
            if (setup.ShowDialog() != true) { Shutdown(); return; }
        }

        var capture = new CaptureService();
        var ocr = new OcrService();
        _llm = new LlmService(_settings);

        _overlay = new OverlayWindow(capture, ocr, _llm, _settings);

        _hotkey = new HotkeyService();
        _hotkey.Register(_settings.HotkeyModifiers, _settings.HotkeyVirtualKey, () => Dispatcher.Invoke(_overlay.OnHotkey));
        // Shift+Q (Modifiers=4=Shift, VK_Q=0x51) to copy last answer
        _hotkey.Register(4, 0x51, () => Dispatcher.Invoke(_overlay.CopyLastAnswer));
        // Ctrl+0 to copy newest answer
        _hotkey.Register(2, 0x30, () => Dispatcher.Invoke(_overlay.CopyLastAnswer));
        // Shift+/ (VK_OEM_2 = 0xBF) to ask about highlighted text
        _hotkey.Register(4, 0xBF, () => Dispatcher.Invoke(_overlay.AskAboutSelection));
        // Ctrl+Shift+/ for "give me the code"
        _hotkey.Register(6, 0xBF, () => Dispatcher.Invoke(_overlay.AskForCode));
        // Ctrl+Shift+4 for Python code
        _hotkey.Register(6, 0x34, () => Dispatcher.Invoke(_overlay.AskForPythonCode));
        // Ctrl+Shift+5 for Java code
        _hotkey.Register(6, 0x35, () => Dispatcher.Invoke(_overlay.AskForJavaCode));
        // Ctrl+1 for Python code ONLY (no explanation)
        _hotkey.Register(2, 0x31, () => Dispatcher.Invoke(_overlay.AskForPythonCodeOnly));
        // Ctrl+7 for Java code ONLY (no explanation)
        _hotkey.Register(2, 0x37, () => Dispatcher.Invoke(_overlay.AskForJavaCodeOnly));
        // Ctrl+S to simplify code
        _hotkey.Register(2, 0x53, () => Dispatcher.Invoke(_overlay.SimplifyCode));
        // Ctrl+N for new chat
        _hotkey.Register(2, 0x4E, () => Dispatcher.Invoke(_overlay.NewChat));
        // Ctrl+Shift+S to open settings (works even when tray is hidden)
        _hotkey.Register(6, 0x53, () => Dispatcher.Invoke(OpenSettings));
        // Ctrl+Shift+T to toggle tray visibility
        _hotkey.Register(6, 0x54, () => Dispatcher.Invoke(ToggleTray));

        SetupTray(ocr);

        // Warm the local model so the first answer is fast (no-op for cloud).
        _ = _llm.WarmAsync();
    }

    private void SetupTray(OcrService ocr)
    {
        // Apply camouflage settings if enabled
        string appName = _settings.CamouflageAsClock ? "clock_1" : "QuickAccessAI";
        string trayText = _settings.CamouflageAsClock ? "clock_1 — Ctrl+Space to read the screen" : "QuickAccessAI — Ctrl+Space to read the screen";

        _tray = new Forms.NotifyIcon
        {
            Icon = LoadAppIcon(),
            Visible = !_settings.HideInTray,
            Text = trayText,
        };
        // Double-clicking the tray icon captures, same as the hotkey.
        _tray.DoubleClick += (_, _) => Dispatcher.Invoke(_overlay.OnHotkey);

        var menu = new Forms.ContextMenuStrip();
        var capture = new Forms.ToolStripMenuItem("Capture now  (Ctrl+Space)", null,
            (_, _) => Dispatcher.Invoke(_overlay.OnHotkey))
        { Font = new System.Drawing.Font(Forms.Control.DefaultFont, System.Drawing.FontStyle.Bold) };
        menu.Items.Add(capture);
        menu.Items.Add(new Forms.ToolStripSeparator());

        // Reopen the setup screen to switch backend or re-enter a cloud key.
        menu.Items.Add("Settings…", null, (_, _) => Dispatcher.Invoke(OpenSettings));

        // Start-with-Windows toggle (per-user Run key; visible in Task Manager).
        var startup = new Forms.ToolStripMenuItem("Start with Windows")
        {
            CheckOnClick = true,
            Checked = StartupService.IsEnabled(),
        };
        startup.CheckedChanged += (_, _) =>
        {
            if (startup.Checked) StartupService.Enable(); else StartupService.Disable();
        };
        menu.Items.Add(startup);

        if (!ocr.IsAvailable)
            menu.Items.Add("⚠ No OCR language installed").Enabled = false;

        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add($"Quit {appName}", null, (_, _) => Shutdown());
        _tray.ContextMenuStrip = menu;
    }

    /// <summary>Reopens the setup screen; changes take effect on the next answer.</summary>
    private void OpenSettings()
    {
        var setup = new SetupWindow(_settings) { Topmost = true };
        if (setup.ShowDialog() == true)
        {
            _ = _llm.WarmAsync(); // warms the local model if they switched to Ollama
            UpdateTrayVisibility();
        }
    }

    private void UpdateTrayVisibility()
    {
        if (_tray != null)
        {
            _tray.Visible = !_settings.HideInTray;
            string appName = _settings.CamouflageAsClock ? "clock_1" : "QuickAccessAI";
            string trayText = _settings.CamouflageAsClock ? "clock_1 — Ctrl+Space to read the screen" : "QuickAccessAI — Ctrl+Space to read the screen";
            _tray.Text = trayText;
            // Update quit menu item text
            if (_tray.ContextMenuStrip != null)
            {
                var quitItem = _tray.ContextMenuStrip.Items.OfType<Forms.ToolStripMenuItem>()
                    .FirstOrDefault(i => i.Text.StartsWith("Quit"));
                if (quitItem != null) quitItem.Text = $"Quit {appName}";
            }
        }
    }

    private void ToggleTray()
    {
        _settings.HideInTray = !_settings.HideInTray;
        _settings.Save();
        UpdateTrayVisibility();
    }

    /// <summary>Loads the embedded app icon for the tray; falls back if missing.</summary>
    private static System.Drawing.Icon LoadAppIcon()
    {
        try
        {
            var asm = System.Reflection.Assembly.GetExecutingAssembly();
            using var s = asm.GetManifestResourceStream("QuickAccessAI.appicon.ico");
            if (s is not null) return new System.Drawing.Icon(s);
        }
        catch { /* fall back below */ }
        return System.Drawing.SystemIcons.Application;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _tray?.Dispose();
        _hotkey?.Dispose();
        base.OnExit(e);
    }
}
