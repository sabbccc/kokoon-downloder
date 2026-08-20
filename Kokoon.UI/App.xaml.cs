using System.Net;
using Microsoft.UI.Xaml;
using Microsoft.UI.Dispatching;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.EntityFrameworkCore;
using Kokoon.Core.Engine;
using Kokoon.Core.Queue;
using Kokoon.Core.Persistence;
using Kokoon.VideoGrabber;
using Kokoon.VideoGrabber.Models;
using Kokoon.UI.Services;
using Kokoon.UI.Settings;
using Kokoon.UI.ViewModels;
using Kokoon.UI.Views;
using Serilog;

namespace Kokoon.UI;

/// <summary>
/// WinUI 3 application entry point. Configures dependency injection,
/// logging, database, system tray, progress bridge, and manages the
/// full application lifecycle including single-instance enforcement
/// and graceful shutdown.
/// </summary>
public partial class App : Application
{
    // ── Static accessors ──────────────────────────────────────────────

    /// <summary>
    /// Global service provider for resolving dependencies throughout the app.
    /// </summary>
    public static IServiceProvider Services { get; private set; } = null!;

    /// <summary>
    /// The main application window. Used by pages that need the HWND
    /// (e.g. file/folder pickers in WinUI 3).
    /// </summary>
    public static Window? MainWindow { get; private set; }

    // ── Instance fields ───────────────────────────────────────────────

    private Window? _window;
    private SystemTrayManager? _trayManager;
    private DownloadProgressBridge? _progressBridge;
    private CancellationTokenSource? _appLifetimeCts;

    public App()
    {
        this.InitializeComponent();

        // ── Global exception handlers ─────────────────────────────────
        this.UnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        // ── Serilog ───────────────────────────────────────────────────
        var logPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "KokoonDownloader", "logs", "kokoon-.log");

        Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.File(logPath,
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 7,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}")
            .CreateLogger();

        // ── DI container ──────────────────────────────────────────────
        var services = new ServiceCollection();

        // Logging
        services.AddLogging(builder =>
        {
            builder.ClearProviders();
            builder.AddSerilog(dispose: true);
        });

        // Settings
        services.AddSingleton<ISettingsService, SettingsService>();

        // HttpClient with SocketsHttpHandler per spec
        services.AddSingleton<HttpClient>(_ => new HttpClient(new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(15),
            MaxConnectionsPerServer = 16,
            EnableMultipleHttp2Connections = true,
            AutomaticDecompression = DecompressionMethods.All
        })
        {
            Timeout = TimeSpan.FromMinutes(30)
        });

        // Core engine services
        services.AddSingleton<SegmentDownloader>();
        services.AddSingleton<SegmentAssembler>();
        services.AddSingleton<DownloadEngine>(sp => new DownloadEngine(
            sp.GetRequiredService<HttpClient>(),
            sp.GetRequiredService<SegmentDownloader>(),
            sp.GetRequiredService<SegmentAssembler>(),
            sp.GetRequiredService<ILogger<DownloadEngine>>()));

        // Queue and scheduler
        services.AddSingleton<IDownloadQueue, DownloadQueue>();

        services.Configure<SchedulerOptions>(opts =>
        {
            // Defaults applied here; LoadAsync may override later.
        });

        // Video grabber services (yt-dlp)
        services.AddSingleton<DefaultBrowserDetector>();
        services.AddSingleton<IVideoCookieAuthProvider, VideoCookieAuthProvider>();
        services.AddSingleton<YtDlpManager>();
        services.AddSingleton<YtDlpProbe>();
        services.AddSingleton<YtDlpUrlExtractor>();
        services.AddSingleton<YtDlpDownloader>();
        services.AddSingleton<FfmpegMuxer>();
        services.AddSingleton<IExternalDownloader, YtDlpFullDownloadHandler>();

        services.AddSingleton<DownloadScheduler>();

        // EF Core / SQLite
        var dbPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "KokoonDownloader", "kokoon.db");

        Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);

        services.AddDbContextFactory<KokoonDbContext>(options =>
            options.UseSqlite($"Data Source={dbPath}"));

        // Repository — singleton using the factory internally
        services.AddSingleton<IDownloadRepository, DownloadRepository>();

        // ViewModels
        services.AddSingleton<MainViewModel>();

        Services = services.BuildServiceProvider();
    }

    /// <summary>
    /// Called by the framework once the app has launched. Creates the main window,
    /// initialises the database, starts the scheduler, and wires up the progress
    /// bridge and system tray.
    /// </summary>
    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        _appLifetimeCts = new CancellationTokenSource();

        try
        {
            // ── Load settings ─────────────────────────────────────────
            var settings = Services.GetRequiredService<ISettingsService>();
            await settings.LoadAsync(_appLifetimeCts.Token);

            // Apply settings to scheduler options.
            var schedulerOptions = Services.GetRequiredService<IOptions<SchedulerOptions>>();
            schedulerOptions.Value.MaxConcurrentDownloads = settings.Current.MaxConcurrentDownloads;
            schedulerOptions.Value.MaxGlobalSpeedBps = settings.Current.SpeedLimitBps;

            // ── Database migration ────────────────────────────────────
            await EnsureDatabaseAsync(_appLifetimeCts.Token);

            // ── Create main window ────────────────────────────────────
            _window = new MainWindow();
            MainWindow = _window;

            // Mica / Acrylic backdrop
            SetBackdrop(_window);

            // Initial window size — compact, IDM-like footprint
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(_window);
            var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
            var appWindow = Microsoft.UI.Windowing.AppWindow.GetFromWindowId(windowId);
            appWindow.Resize(new Windows.Graphics.SizeInt32(900, 600));

            // ── System tray ───────────────────────────────────────────
            var dispatcherQueue = DispatcherQueue.GetForCurrentThread();

            _trayManager = new SystemTrayManager(
                Services.GetRequiredService<ILogger<SystemTrayManager>>(),
                dispatcherQueue);

            _trayManager.ShowWindowRequested += OnTrayShowWindow;
            _trayManager.PauseAllRequested += OnTrayPauseAll;
            _trayManager.ResumeAllRequested += OnTrayResumeAll;
            _trayManager.ExitRequested += OnTrayExit;
            _trayManager.Initialize();

            // ── Close intercept for minimize-to-tray ──────────────────
            appWindow.Closing += (s, e) =>
            {
                if (settings.Current.MinimizeToTray)
                {
                    e.Cancel = true;
                    _window.AppWindow.Hide();
                }
            };

            // ── Progress bridge ───────────────────────────────────────
            _progressBridge = new DownloadProgressBridge(
                Services.GetRequiredService<DownloadScheduler>(),
                Services.GetRequiredService<MainViewModel>(),
                dispatcherQueue,
                Services.GetRequiredService<ILogger<DownloadProgressBridge>>());

            _progressBridge.Start();

            // ── Start scheduler ───────────────────────────────────────
            var scheduler = Services.GetRequiredService<DownloadScheduler>();
            await scheduler.StartAsync(_appLifetimeCts.Token);

            // ── Load persisted downloads into the VM ──────────────────
            var mainVm = Services.GetRequiredService<MainViewModel>();
            await mainVm.LoadDownloadsAsync(_appLifetimeCts.Token);

            // ── Apply saved theme on UI thread before showing window ──
            var savedTheme = settings.Current.Theme;
            var baseTheme = settings.Current.BaseTheme;
            if (!string.IsNullOrEmpty(savedTheme))
            {
                var applied = new SemaphoreSlim(0, 1);
                dispatcherQueue.TryEnqueue(() =>
                {
                    ApplyTheme(savedTheme, baseTheme);
                    applied.Release();
                });
                applied.Wait(TimeSpan.FromSeconds(3));
            }

            // ── Show window ───────────────────────────────────────────
            _window.Activate();

            // ── Background yt-dlp update check ────────────────────────
            _ = Task.Run(async () =>
            {
                try
                {
                    var activeScheduler = Services.GetRequiredService<DownloadScheduler>();
                    if (activeScheduler.ActiveCount > 0)
                        return;

                    // yt-dlp is invoked fresh per probe/download, so no app
                    // restart is needed after a self-update — just notify.
                    var ytDlpManager = Services.GetRequiredService<YtDlpManager>();
                    var result = await ytDlpManager.CheckForUpdateAsync(_appLifetimeCts.Token);

                    if (result.Status == YtDlpUpdateStatus.Updated)
                    {
                        var version = string.IsNullOrEmpty(result.NewVersion)
                            ? string.Empty
                            : $" to {result.NewVersion}";
                        _trayManager?.ShowBalloonNotification("Kokoon Downloader", $"yt-dlp updated{version}");
                    }
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "yt-dlp background update check failed");
                }
            });
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Fatal error during app startup");
            Exit();
        }
    }

    // ── Database ──────────────────────────────────────────────────────

    /// <summary>
    /// Ensures the SQLite database exists and is up to date with the latest
    /// EF Core migration. Uses <see cref="IDbContextFactory{TContext}"/>
    /// to avoid scoping issues.
    /// </summary>
    private static async Task EnsureDatabaseAsync(CancellationToken ct)
    {
        var factory = Services.GetRequiredService<IDbContextFactory<KokoonDbContext>>();
        await using var ctx = await factory.CreateDbContextAsync(ct);
        await ctx.Database.MigrateAsync(ct);
    }

    // ── Backdrop ──────────────────────────────────────────────────────

    private static void SetBackdrop(Window window)
    {
        try
        {
            window.SystemBackdrop = new Microsoft.UI.Xaml.Media.MicaBackdrop();
        }
        catch
        {
            try
            {
                window.SystemBackdrop = new Microsoft.UI.Xaml.Media.DesktopAcrylicBackdrop();
            }
            catch
            {
                // No backdrop support — window uses its default background.
            }
        }
    }

    // ── Tray event handlers ───────────────────────────────────────────

    private void OnTrayShowWindow()
    {
        if (_window is null) return;

        _window.AppWindow.Show();
        _window.Activate();

        // Bring to foreground.
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(_window);
        SetForegroundWindowPInvoke(hwnd);
    }

    private void OnTrayPauseAll()
    {
        var vm = Services.GetRequiredService<MainViewModel>();
        vm.PauseAllCommand.Execute(null);
    }

    private void OnTrayResumeAll()
    {
        var vm = Services.GetRequiredService<MainViewModel>();
        vm.ResumeAllCommand.Execute(null);
    }

    private async void OnTrayExit()
    {
        await ShutdownGracefullyAsync();
        Exit();
    }

    // ── Theme switching ─────────────────────────────────────────

    /// <summary>
    /// Switches the effective Dark/Light/System mode. Brush values are
    /// resolved live via <c>{ThemeResource}</c> against
    /// <c>Kokoon.UI/Themes/NeonDark.xaml</c>'s <c>ThemeDictionaries</c>, so
    /// this takes effect immediately for already-rendered UI (including open
    /// dialogs) — no restart needed. <paramref name="themeName"/> is unused
    /// (Neon is the only theme pack) and kept only for call-site compatibility.
    /// </summary>
    public static void ApplyTheme(string themeName, BaseThemeMode mode)
    {
        var elementTheme = mode switch
        {
            BaseThemeMode.Light => ElementTheme.Light,
            BaseThemeMode.System => ElementTheme.Default,
            _ => ElementTheme.Dark,
        };

        if (MainWindow?.Content is FrameworkElement root)
        {
            // Toggle through a different value first to guarantee a change
            // event fires even when the effective mode hasn't changed (e.g. Dark → Dark).
            root.RequestedTheme = elementTheme == ElementTheme.Default
                ? ElementTheme.Dark
                : ElementTheme.Default;
            root.RequestedTheme = elementTheme;
        }
    }

    // ── Graceful shutdown ─────────────────────────────────────────────

    /// <summary>
    /// Stops the scheduler (saving progress for active downloads), disposes
    /// the tray icon and progress bridge, and flushes logs.
    /// </summary>
    private async Task ShutdownGracefullyAsync()
    {
        Log.Information("Application shutting down gracefully");

        try
        {
            _appLifetimeCts?.Cancel();

            // Stop the scheduler — this saves progress for all active downloads.
            var scheduler = Services.GetRequiredService<DownloadScheduler>();
            await scheduler.StopAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error during scheduler shutdown");
        }

        _progressBridge?.Dispose();
        _trayManager?.Dispose();

        await Log.CloseAndFlushAsync();
    }

    // ── Exception handlers ────────────────────────────────────────────

    private void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        Log.Error(e.Exception, "Unhandled UI exception: {Message}", e.Message);
        e.Handled = true;
    }

    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        Log.Error(e.Exception, "Unobserved task exception");
        e.SetObserved();
    }

    // ── P/Invoke for foreground activation ─────────────────────────────

    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "SetForegroundWindow")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool SetForegroundWindowPInvoke(IntPtr hWnd);
}
