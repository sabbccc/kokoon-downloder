using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Dispatching;

namespace Kokoon.UI.Services;

/// <summary>
/// Manages a Win32 system tray (notification area) icon using Shell_NotifyIcon
/// P/Invoke. WinUI 3 has no built-in tray icon API, so we create a hidden
/// message-only window to receive tray callbacks and dispatch them onto the
/// UI thread via <see cref="DispatcherQueue"/>.
/// </summary>
public sealed class SystemTrayManager : IDisposable
{
    // ── Win32 constants ───────────────────────────────────────────────
    private const int WM_USER = 0x0400;
    private const int WM_TRAYICON = WM_USER + 1;
    private const int WM_DESTROY = 0x0002;

    private const int WM_LBUTTONDBLCLK = 0x0203;
    private const int WM_RBUTTONUP = 0x0205;

    private const int NIF_MESSAGE = 0x01;
    private const int NIF_ICON = 0x02;
    private const int NIF_TIP = 0x04;
    private const int NIF_INFO = 0x10;
    private const int NIM_ADD = 0x00;
    private const int NIM_MODIFY = 0x01;
    private const int NIM_DELETE = 0x02;
    private const int NIIF_INFO = 0x01;

    private const int TPM_RIGHTALIGN = 0x0008;
    private const int TPM_BOTTOMALIGN = 0x0020;
    private const int TPM_RETURNCMD = 0x0100;

    private const int MF_STRING = 0x0000;
    private const int MF_SEPARATOR = 0x0800;

    // Menu item IDs
    private const int CMD_PAUSE_ALL = 1001;
    private const int CMD_RESUME_ALL = 1002;
    private const int CMD_OPEN = 1003;
    private const int CMD_EXIT = 1004;

    // ── P/Invoke ──────────────────────────────────────────────────────

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATA
    {
        public int cbSize;
        public IntPtr hWnd;
        public int uID;
        public int uFlags;
        public int uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szTip;
        public int dwState;
        public int dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string szInfo;
        // Win32 union: version number (pre-Vista) or balloon timeout in ms.
        public uint uTimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string szInfoTitle;
        public int dwInfoFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WNDCLASSEX
    {
        public int cbSize;
        public int style;
        public WndProcDelegate lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string? lpszMenuName;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string lpszClassName;
        public IntPtr hIconSm;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public POINT pt;
    }

    private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Shell_NotifyIcon(int dwMessage, ref NOTIFYICONDATA lpData);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern ushort RegisterClassEx(ref WNDCLASSEX lpwcx);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateWindowEx(
        int dwExStyle, string lpClassName, string lpWindowName,
        int dwStyle, int x, int y, int nWidth, int nHeight,
        IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);

    [DllImport("user32.dll")]
    private static extern IntPtr DefWindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TranslateMessage(ref MSG lpMsg);

    [DllImport("user32.dll")]
    private static extern IntPtr DispatchMessage(ref MSG lpMsg);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AppendMenu(IntPtr hMenu, int uFlags, int uIDNewItem, string? lpNewItem);

    [DllImport("user32.dll")]
    private static extern int TrackPopupMenu(IntPtr hMenu, int uFlags, int x, int y, int nReserved, IntPtr hWnd, IntPtr prcRect);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyMenu(IntPtr hMenu);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    [DllImport("user32.dll")]
    private static extern IntPtr LoadIcon(IntPtr hInstance, IntPtr lpIconName);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr ExtractIcon(IntPtr hInst, string lpszExeFileName, int nIconIndex);

    // ── Events ────────────────────────────────────────────────────────

    /// <summary>Fired when the user selects "Pause All" from the tray menu.</summary>
    public event Action? PauseAllRequested;

    /// <summary>Fired when the user selects "Resume All" from the tray menu.</summary>
    public event Action? ResumeAllRequested;

    /// <summary>Fired when the user wants to bring the main window to the foreground.</summary>
    public event Action? ShowWindowRequested;

    /// <summary>Fired when the user selects "Exit" from the tray menu.</summary>
    public event Action? ExitRequested;

    // ── Fields ─────────────────────────────────────────────────────────

    private readonly ILogger<SystemTrayManager> _logger;
    private readonly DispatcherQueue _dispatcherQueue;
    private IntPtr _messageWindow;
    private IntPtr _iconHandle;
    private NOTIFYICONDATA _notifyData;
    private Thread? _messageThread;
    private volatile bool _disposed;

    // Must hold a reference to prevent GC of the delegate.
    private WndProcDelegate? _wndProcDelegate;

    /// <summary>
    /// Initializes a new instance of <see cref="SystemTrayManager"/>.
    /// </summary>
    /// <param name="logger">Logger for diagnostics.</param>
    /// <param name="dispatcherQueue">The UI thread's dispatcher queue.</param>
    public SystemTrayManager(ILogger<SystemTrayManager> logger, DispatcherQueue dispatcherQueue)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _dispatcherQueue = dispatcherQueue ?? throw new ArgumentNullException(nameof(dispatcherQueue));
    }

    /// <summary>
    /// Creates the tray icon and starts a background message pump thread.
    /// Call once after the main window is created.
    /// </summary>
    public void Initialize()
    {
        _messageThread = new Thread(MessagePumpThread)
        {
            Name = "KokoonTrayIconThread",
            IsBackground = true
        };
        _messageThread.SetApartmentState(ApartmentState.STA);
        _messageThread.Start();

        _logger.LogInformation("System tray icon initialized");
    }

    private void MessagePumpThread()
    {
        try
        {
            var hInstance = GetModuleHandle(null);
            var className = "KokoonTrayWndClass_" + Environment.ProcessId;

            _wndProcDelegate = WndProc;

            var wndClass = new WNDCLASSEX
            {
                cbSize = Marshal.SizeOf<WNDCLASSEX>(),
                lpfnWndProc = _wndProcDelegate,
                hInstance = hInstance,
                lpszClassName = className
            };

            RegisterClassEx(ref wndClass);

            // HWND_MESSAGE (-3) creates a message-only window — no visible UI.
            _messageWindow = CreateWindowEx(
                0, className, "KokoonTrayWindow",
                0, 0, 0, 0, 0,
                new IntPtr(-3), IntPtr.Zero, hInstance, IntPtr.Zero);

            // Load the app icon embedded in the exe (set via ApplicationIcon in .csproj).
            var exePath = Environment.ProcessPath;
            if (exePath != null)
                _iconHandle = ExtractIcon(IntPtr.Zero, exePath, 0);
            if (_iconHandle == IntPtr.Zero)
                _iconHandle = LoadIcon(IntPtr.Zero, new IntPtr(32512));

            _notifyData = new NOTIFYICONDATA
            {
                cbSize = Marshal.SizeOf<NOTIFYICONDATA>(),
                hWnd = _messageWindow,
                uID = 1,
                uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP,
                uCallbackMessage = WM_TRAYICON,
                hIcon = _iconHandle,
                szTip = "Kokoon Downloader"
            };

            Shell_NotifyIcon(NIM_ADD, ref _notifyData);

            // Win32 message loop.
            while (!_disposed && GetMessage(out var msg, IntPtr.Zero, 0, 0))
            {
                TranslateMessage(ref msg);
                DispatchMessage(ref msg);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Tray icon message pump crashed");
        }
    }

    private IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == WM_TRAYICON)
        {
            var mouseMsg = (int)lParam;

            if (mouseMsg == WM_LBUTTONDBLCLK)
            {
                _dispatcherQueue.TryEnqueue(() => ShowWindowRequested?.Invoke());
            }
            else if (mouseMsg == WM_RBUTTONUP)
            {
                ShowContextMenu(hWnd);
            }

            return IntPtr.Zero;
        }

        return DefWindowProc(hWnd, msg, wParam, lParam);
    }

    private void ShowContextMenu(IntPtr hWnd)
    {
        var hMenu = CreatePopupMenu();

        AppendMenu(hMenu, MF_STRING, CMD_PAUSE_ALL, "Pause All");
        AppendMenu(hMenu, MF_STRING, CMD_RESUME_ALL, "Resume All");
        AppendMenu(hMenu, MF_SEPARATOR, 0, null);
        AppendMenu(hMenu, MF_STRING, CMD_OPEN, "Open Kokoon");
        AppendMenu(hMenu, MF_SEPARATOR, 0, null);
        AppendMenu(hMenu, MF_STRING, CMD_EXIT, "Exit");

        GetCursorPos(out var pt);

        // SetForegroundWindow is required before TrackPopupMenu so the
        // menu dismisses correctly when clicking elsewhere.
        SetForegroundWindow(hWnd);

        // TPM_RETURNCMD makes TrackPopupMenu return the selected command
        // ID directly, rather than posting WM_COMMAND.
        var cmdId = TrackPopupMenu(hMenu,
            TPM_RIGHTALIGN | TPM_BOTTOMALIGN | TPM_RETURNCMD,
            pt.X, pt.Y, 0, hWnd, IntPtr.Zero);

        DestroyMenu(hMenu);

        // Dispatch the selected command to the UI thread.
        switch (cmdId)
        {
            case CMD_PAUSE_ALL:
                _dispatcherQueue.TryEnqueue(() => PauseAllRequested?.Invoke());
                break;
            case CMD_RESUME_ALL:
                _dispatcherQueue.TryEnqueue(() => ResumeAllRequested?.Invoke());
                break;
            case CMD_OPEN:
                _dispatcherQueue.TryEnqueue(() => ShowWindowRequested?.Invoke());
                break;
            case CMD_EXIT:
                _dispatcherQueue.TryEnqueue(() => ExitRequested?.Invoke());
                break;
        }
    }

    /// <summary>
    /// Updates the tray icon tooltip text (e.g., to show active download count).
    /// </summary>
    /// <param name="tooltip">New tooltip string (max 127 characters).</param>
    public void UpdateTooltip(string tooltip)
    {
        if (_messageWindow == IntPtr.Zero) return;

        _notifyData.szTip = tooltip.Length > 127 ? tooltip[..127] : tooltip;
        _notifyData.uFlags = NIF_TIP;
        Shell_NotifyIcon(NIM_MODIFY, ref _notifyData);
    }

    /// <summary>
    /// Shows a tray balloon notification. Purely informational — no click
    /// handling is wired up.
    /// </summary>
    /// <param name="title">Balloon title (max 63 characters).</param>
    /// <param name="message">Balloon body text (max 255 characters).</param>
    public void ShowBalloonNotification(string title, string message)
    {
        if (_messageWindow == IntPtr.Zero) return;

        _notifyData.szInfoTitle = title.Length > 63 ? title[..63] : title;
        _notifyData.szInfo = message.Length > 255 ? message[..255] : message;
        _notifyData.dwInfoFlags = NIIF_INFO;
        _notifyData.uFlags = NIF_INFO;
        Shell_NotifyIcon(NIM_MODIFY, ref _notifyData);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        try
        {
            if (_messageWindow != IntPtr.Zero)
            {
                Shell_NotifyIcon(NIM_DELETE, ref _notifyData);
                PostMessage(_messageWindow, WM_DESTROY, IntPtr.Zero, IntPtr.Zero);
                // Post WM_QUIT to exit the GetMessage loop.
                PostMessage(_messageWindow, 0x0012 /* WM_QUIT */, IntPtr.Zero, IntPtr.Zero);
            }

            _messageThread?.Join(TimeSpan.FromSeconds(2));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error disposing system tray icon");
        }
    }
}
