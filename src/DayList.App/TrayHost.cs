using System.Runtime.InteropServices;
using DayList.Core;
using Microsoft.UI.Xaml;
using WinRT.Interop;

namespace DayList;

internal sealed class TrayHost : IDisposable
{
    private readonly Window _window;
    private readonly Action _onShow;
    private readonly Action _onQuickAdd;
    private readonly Action _onExit;
    private readonly NativeShell.WndProc _wndProc;
    private IntPtr _messageHwnd;
    private IntPtr _iconHandle;
    private bool _trayAdded;
    private bool _hotkeyRegistered;
    private bool _ownedIcon;
    private bool _disposed;
    private GCHandle _procHandle;
    private string _className = "";

    public TrayHost(Window window, Action onShow, Action onQuickAdd, Action onExit)
    {
        _window = window;
        _onShow = onShow;
        _onQuickAdd = onQuickAdd;
        _onExit = onExit;
        _wndProc = WndProcImpl;
        _procHandle = GCHandle.Alloc(_wndProc);
        CreateMessageWindow();
        AddTrayIcon();
    }

    public bool IsReady => _messageHwnd != IntPtr.Zero;

    public bool IsTrayVisible => _trayAdded;

    public bool IsHotkeyRegistered => _hotkeyRegistered;

    public string? LastHotkeyError { get; private set; }

    public IntPtr WindowHandle => WindowNative.GetWindowHandle(_window);

    public bool RegisterHotkey(HotkeySpec hotkey)
    {
        UnregisterHotkey();
        LastHotkeyError = null;

        if (_messageHwnd == IntPtr.Zero)
        {
            LastHotkeyError = "托盘消息窗口未创建，无法注册热键。";
            return false;
        }

        // Prefer MOD_NOREPEAT; fall back without it on older hosts.
        if (TryRegister(hotkey, withNoRepeat: true) || TryRegister(hotkey, withNoRepeat: false))
        {
            _hotkeyRegistered = true;
            return true;
        }

        var err = NativeShell.GetLastError();
        LastHotkeyError = err == 1409
            ? $"热键 {hotkey.ToDisplay()} 已被其他程序占用，请在设置里换一组。"
            : $"热键 {hotkey.ToDisplay()} 注册失败（错误码 {err}）。";
        return false;
    }

    private bool TryRegister(HotkeySpec hotkey, bool withNoRepeat)
    {
        uint mods = 0;
        if (hotkey.Alt)
            mods |= 0x0001;
        if (hotkey.Ctrl)
            mods |= 0x0002;
        if (hotkey.Shift)
            mods |= 0x0004;
        if (withNoRepeat)
            mods |= 0x4000;

        return NativeShell.RegisterHotKey(
            _messageHwnd,
            NativeShell.HotkeyId,
            mods,
            (uint)hotkey.VirtualKey);
    }

    public void UnregisterHotkey()
    {
        if (!_hotkeyRegistered || _messageHwnd == IntPtr.Zero)
            return;
        NativeShell.UnregisterHotKey(_messageHwnd, NativeShell.HotkeyId);
        _hotkeyRegistered = false;
    }

    public void EnsureTrayIcon()
    {
        if (_trayAdded || _messageHwnd == IntPtr.Zero)
            return;
        AddTrayIcon();
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        UnregisterHotkey();
        RemoveTrayIcon();
        if (_messageHwnd != IntPtr.Zero)
        {
            NativeShell.DestroyWindow(_messageHwnd);
            _messageHwnd = IntPtr.Zero;
        }

        if (_ownedIcon && _iconHandle != IntPtr.Zero)
        {
            NativeShell.DestroyIcon(_iconHandle);
            _iconHandle = IntPtr.Zero;
        }

        if (_procHandle.IsAllocated)
            _procHandle.Free();
    }

    private void CreateMessageWindow()
    {
        _className = "DayListTrayHost_" + Guid.NewGuid().ToString("N");
        var hInstance = NativeShell.GetModuleHandleW(null);
        var wndClass = new NativeShell.WndClassExW
        {
            cbSize = (uint)Marshal.SizeOf<NativeShell.WndClassExW>(),
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProc),
            hInstance = hInstance,
            lpszClassName = _className
        };

        var atom = NativeShell.RegisterClassExW(ref wndClass);
        if (atom == 0)
        {
            // Class may already exist in rare races; still try CreateWindowEx.
            var err = NativeShell.GetLastError();
            if (err != 1410) // ERROR_CLASS_ALREADY_EXISTS
                return;
        }

        // HWND_MESSAGE = -3. Message-only window is enough for tray + hotkey.
        _messageHwnd = NativeShell.CreateWindowExW(
            0,
            _className,
            "DayListTray",
            0,
            0,
            0,
            0,
            0,
            new IntPtr(-3),
            IntPtr.Zero,
            hInstance,
            IntPtr.Zero);

        if (_messageHwnd != IntPtr.Zero)
            return;

        // Fallback: tiny hidden overlapped window if message-only creation fails.
        _messageHwnd = NativeShell.CreateWindowExW(
            0x00000080, // WS_EX_TOOLWINDOW
            _className,
            "DayListTray",
            0x80000000, // WS_POPUP
            0,
            0,
            0,
            0,
            IntPtr.Zero,
            IntPtr.Zero,
            hInstance,
            IntPtr.Zero);
    }

    private void AddTrayIcon()
    {
        if (_messageHwnd == IntPtr.Zero)
            return;

        if (_iconHandle == IntPtr.Zero)
            _iconHandle = LoadIconHandle();

        var data = new NativeShell.NotifyIconData
        {
            cbSize = Marshal.SizeOf<NativeShell.NotifyIconData>(),
            hWnd = _messageHwnd,
            uID = 1,
            uFlags = NativeShell.NifMessage | NativeShell.NifIcon | NativeShell.NifTip,
            uCallbackMessage = NativeShell.WmAppTray,
            hIcon = _iconHandle,
            szTip = "DayList"
        };

        _trayAdded = NativeShell.Shell_NotifyIconW(NativeShell.NimAdd, ref data);
        if (!_trayAdded)
        {
            // Explorer may not be ready yet; one modify/add retry helps.
            NativeShell.Shell_NotifyIconW(NativeShell.NimModify, ref data);
            _trayAdded = NativeShell.Shell_NotifyIconW(NativeShell.NimAdd, ref data);
        }
    }

    private void RemoveTrayIcon()
    {
        if (!_trayAdded || _messageHwnd == IntPtr.Zero)
            return;

        var data = new NativeShell.NotifyIconData
        {
            cbSize = Marshal.SizeOf<NativeShell.NotifyIconData>(),
            hWnd = _messageHwnd,
            uID = 1
        };
        NativeShell.Shell_NotifyIconW(NativeShell.NimDelete, ref data);
        _trayAdded = false;
    }

    private IntPtr LoadIconHandle()
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "Assets", "DayList.ico");
            if (!File.Exists(path))
                path = Path.Combine(AppContext.BaseDirectory, "DayList.ico");
            if (File.Exists(path))
            {
                var handle = LoadImage(IntPtr.Zero, path, 1, 0, 0, 0x00000010 | 0x00008000);
                if (handle != IntPtr.Zero)
                {
                    _ownedIcon = true;
                    return handle;
                }
            }
        }
        catch
        {
            // Fall through to stock icon.
        }

        _ownedIcon = false;
        // IDI_APPLICATION
        return NativeShell.LoadIconW(IntPtr.Zero, new IntPtr(32512));
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LoadImage(IntPtr hInst, string name, uint type, int cx, int cy, uint fuLoad);

    private IntPtr WndProcImpl(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == NativeShell.WmHotkey && wParam.ToInt32() == NativeShell.HotkeyId)
        {
            Dispatch(_onShow);
            Dispatch(_onQuickAdd);
            return IntPtr.Zero;
        }

        if (msg == NativeShell.WmAppTray)
        {
            var mouse = lParam.ToInt32() & 0xFFFF;
            if (mouse == NativeShell.WmLButtonDblClk || mouse == 0x0202) // WM_LBUTTONUP
            {
                Dispatch(_onShow);
                return IntPtr.Zero;
            }

            if (mouse == NativeShell.WmRButtonUp)
            {
                ShowContextMenu();
                return IntPtr.Zero;
            }
        }

        if (msg == NativeShell.WmCommand)
        {
            switch (wParam.ToInt32() & 0xFFFF)
            {
                case NativeShell.CmdShow:
                    Dispatch(_onShow);
                    break;
                case NativeShell.CmdQuickAdd:
                    Dispatch(_onShow);
                    Dispatch(_onQuickAdd);
                    break;
                case NativeShell.CmdExit:
                    Dispatch(_onExit);
                    break;
            }

            return IntPtr.Zero;
        }

        return NativeShell.DefWindowProcW(hWnd, msg, wParam, lParam);
    }

    private void ShowContextMenu()
    {
        var menu = NativeShell.CreatePopupMenu();
        NativeShell.AppendMenuW(menu, 0, NativeShell.CmdShow, "显示 DayList");
        NativeShell.AppendMenuW(menu, 0, NativeShell.CmdQuickAdd, "快速添加");
        NativeShell.AppendMenuW(menu, 0x0800, 0, string.Empty); // MF_SEPARATOR
        NativeShell.AppendMenuW(menu, 0, NativeShell.CmdExit, "退出");
        NativeShell.GetCursorPos(out var pt);
        NativeShell.SetForegroundWindow(_messageHwnd);
        NativeShell.TrackPopupMenu(menu, 0x0002, pt.X, pt.Y, 0, _messageHwnd, IntPtr.Zero);
        NativeShell.DestroyMenu(menu);
        NativeShell.PostMessageW(_messageHwnd, 0, IntPtr.Zero, IntPtr.Zero);
    }

    private void Dispatch(Action action)
    {
        var dq = _window.DispatcherQueue;
        if (dq is null)
            return;
        dq.TryEnqueue(() => action());
    }
}