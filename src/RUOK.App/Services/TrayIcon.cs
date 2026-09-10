using System.ComponentModel;
using System.Runtime.InteropServices;
using RUOK_App.Resources;

namespace RUOK_App.Services;

public sealed class TrayIcon : IDisposable
{
    private const uint CallbackMessage = 0x8001;
    private readonly WindowProcedure _procedure;
    private readonly string _className = $"RUOK.Tray.{Environment.ProcessId}";
    private readonly nint _module;
    private readonly nint _window;
    private readonly nint _icon;
    private readonly uint _taskbarCreated;
    private bool _disposed;

    public event Action? OpenRequested;
    public event Action? QuitRequested;
    public event Action<string>? Error;

    public TrayIcon()
    {
        _procedure = HandleMessage;
        _module = GetModuleHandle(null);
        _taskbarCreated = RegisterWindowMessage("TaskbarCreated");
        var windowClass = new WindowClass
        {
            Size = (uint)Marshal.SizeOf<WindowClass>(),
            Procedure = Marshal.GetFunctionPointerForDelegate(_procedure),
            Instance = _module,
            ClassName = _className
        };
        if (RegisterClassEx(ref windowClass) == 0)
            throw new Win32Exception(Marshal.GetLastWin32Error());
        _window = CreateWindowEx(0, _className, "", 0, 0, 0, 0, 0, 0, 0, _module, 0);
        if (_window == 0)
        {
            UnregisterClass(_className, _module);
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }
        _icon = LoadImage(0, Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico"), 1, 0, 0, 0x10 | 0x40);
        if (_icon == 0)
        {
            DestroyWindow(_window);
            UnregisterClass(_className, _module);
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }
        if (!AddIcon())
        {
            Dispose();
            throw new Win32Exception(UiText.Get("TrayUnavailable"));
        }
    }

    private NotifyIconData Data() => new()
    {
        Size = (uint)Marshal.SizeOf<NotifyIconData>(),
        Window = _window,
        Id = 1,
        Flags = 1 | 2 | 4 | 0x80,
        Callback = CallbackMessage,
        Icon = _icon,
        Tip = UiText.Get("TrayTooltip"),
        Info = "",
        InfoTitle = "",
        Version = 4
    };

    private bool AddIcon()
    {
        var data = Data();
        return ShellNotifyIcon(0, ref data) && ShellNotifyIcon(4, ref data);
    }

    private nint HandleMessage(nint window, uint message, nuint wParam, nint lParam)
    {
        if (_disposed)
            return DefWindowProc(window, message, wParam, lParam);
        try
        {
            if (message == _taskbarCreated && _window != 0)
            {
                if (!AddIcon())
                    Error?.Invoke(UiText.Get("TrayUnavailable"));
            }
            else if (message == CallbackMessage)
            {
                var action = (uint)((long)lParam & 0xFFFF);
                if (action is 0x400 or 0x401)
                    OpenRequested?.Invoke();
                else if (action == 0x7B)
                    ShowMenu();
                return 0;
            }
        }
        catch (Exception error) when (error is Win32Exception or COMException)
        {
            Error?.Invoke(UiText.Get("TrayUnavailable"));
        }
        return DefWindowProc(window, message, wParam, lParam);
    }

    private void ShowMenu()
    {
        var menu = CreatePopupMenu();
        if (menu == 0)
            throw new Win32Exception(Marshal.GetLastWin32Error());
        uint command;
        try
        {
            if (!AppendMenu(menu, 0, 1, UiText.Get("TrayOpen"))
                || !AppendMenu(menu, 0, 2, UiText.Get("TrayQuit"))
                || !GetCursorPos(out var point))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            SetForegroundWindow(_window);
            command = TrackPopupMenu(menu, 0x100 | 0x2, point.X, point.Y, 0, _window, 0);
            PostMessage(_window, 0, 0, 0);
        }
        finally
        {
            DestroyMenu(menu);
        }
        if (command == 1)
            OpenRequested?.Invoke();
        else if (command == 2)
            QuitRequested?.Invoke();
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        var data = Data();
        ShellNotifyIcon(2, ref data);
        DestroyWindow(_window);
        DestroyIcon(_icon);
        UnregisterClass(_className, _module);
        GC.KeepAlive(_procedure);
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate nint WindowProcedure(nint window, uint message, nuint wParam, nint lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WindowClass
    {
        public uint Size, Style;
        public nint Procedure;
        public int ClassExtra, WindowExtra;
        public nint Instance, Icon, Cursor, Background;
        public string? MenuName;
        public string ClassName;
        public nint SmallIcon;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyIconData
    {
        public uint Size;
        public nint Window;
        public uint Id, Flags, Callback;
        public nint Icon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Tip;
        public uint State, StateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Info;
        public uint Version;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string InfoTitle;
        public uint InfoFlags;
        public Guid Guid;
        public nint BalloonIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point { public int X, Y; }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern nint GetModuleHandle(string? name);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern ushort RegisterClassEx(ref WindowClass windowClass);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool UnregisterClass(string name, nint instance);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern nint CreateWindowEx(uint extendedStyle, string className, string name, uint style, int x, int y, int width, int height, nint parent, nint menu, nint instance, nint parameter);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint DefWindowProc(nint window, uint message, nuint wParam, nint lParam);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern uint RegisterWindowMessage(string message);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern nint LoadImage(nint instance, string path, uint type, int width, int height, uint flags);
    [DllImport("shell32.dll", EntryPoint = "Shell_NotifyIconW", CharSet = CharSet.Unicode)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool ShellNotifyIcon(uint message, ref NotifyIconData data);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DestroyWindow(nint window);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DestroyIcon(nint icon);
    [DllImport("user32.dll", SetLastError = true)] private static extern nint CreatePopupMenu();
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool AppendMenu(nint menu, uint flags, nuint id, string label);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DestroyMenu(nint menu);
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll")] private static extern uint TrackPopupMenu(nint menu, uint flags, int x, int y, int reserved, nint owner, nint rectangle);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool SetForegroundWindow(nint window);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool PostMessage(nint window, uint message, nuint wParam, nint lParam);
}
