using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace ScreenAssistant.Services;

/// <summary>
/// Registers system-wide hotkeys and raises events when they fire.
/// Uses a message-only window so it works without any visible window focused.
/// </summary>
public sealed class HotkeyService : IDisposable
{
    private const int WM_HOTKEY = 0x0312;
    private static readonly IntPtr HWND_MESSAGE = new(-3);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private readonly HwndSource _source;
    private readonly Dictionary<int, Action> _callbacks = new();
    private int _nextId = 1;

    public HotkeyService()
    {
        var parameters = new HwndSourceParameters("ScreenAssistantHotkey")
        {
            ParentWindow = HWND_MESSAGE,
            Width = 0,
            Height = 0,
        };
        _source = new HwndSource(parameters);
        _source.AddHook(WndProc);
    }

    /// <summary>
    /// Registers a hotkey. Returns an ID that can be used to unregister later.
    /// Returns 0 if the combination is already taken by another app.
    /// </summary>
    public int Register(uint modifiers, uint virtualKey, Action callback)
    {
        int id = _nextId++;
        bool ok = RegisterHotKey(_source.Handle, id, modifiers, virtualKey);
        if (ok)
        {
            _callbacks[id] = callback;
            return id;
        }
        return 0;
    }

    public void Unregister(int id)
    {
        if (_callbacks.Remove(id))
        {
            UnregisterHotKey(_source.Handle, id);
        }
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY)
        {
            int id = wParam.ToInt32();
            if (_callbacks.TryGetValue(id, out var callback))
            {
                callback?.Invoke();
                handled = true;
            }
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        foreach (var id in _callbacks.Keys)
        {
            UnregisterHotKey(_source.Handle, id);
        }
        _callbacks.Clear();
        _source.RemoveHook(WndProc);
        _source.Dispose();
    }
}
