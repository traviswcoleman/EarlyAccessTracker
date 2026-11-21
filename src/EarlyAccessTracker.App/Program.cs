using EarlyAccessTracker.App;
using EarlyAccessTracker.App.Components;
using EarlyAccessTracker.Core;
using EarlyAccessTracker.Core.Data;
using EarlyAccessTracker.Core.Steam;
using EarlyAccessTracker.Core.Sync;
using Microsoft.EntityFrameworkCore;

namespace EarlyAccessTracker.App;

public static class Program
{
    private const string MutexName = @"Local\EarlyAccessTracker.SingleInstance";
    private const string ShowEventName = @"Local\EarlyAccessTracker.ShowDashboard";

    /// <summary>
    /// Usage: EarlyAccessTracker.exe [--background]
    /// --background (used by the scheduled task) starts silently in the tray; without it the dashboard opens.
    /// A second launch never starts a second sync engine: it just asks the running instance to open the dashboard
    /// (or, with --background, exits quietly).
    /// </summary>
    [STAThread]
    public static int Main(string[] args)
    {
        DebugConsole.HideIfOwnedByVisualStudio();
        var background = args.Any(a => a.Equals("--background", StringComparison.OrdinalIgnoreCase));

        using var mutex = new Mutex(initiallyOwned: true, MutexName, out var isFirstInstance);
        using var showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        if (!isFirstInstance)
        {
            if (!background) showEvent.Set();
            return 0;
        }

        try
        {
            Directory.CreateDirectory(AppPaths.DataDirectory);
            var app = BuildWebApp(args.Where(a => !a.StartsWith("--background", StringComparison.OrdinalIgnoreCase)).ToArray());

            using (var scope = app.Services.CreateScope())
                scope.ServiceProvider.GetRequiredService<TrackerDbContext>().Database.Migrate();

            app.StartAsync().GetAwaiter().GetResult();

            System.Windows.Forms.Application.SetHighDpiMode(System.Windows.Forms.HighDpiMode.PerMonitorV2);
            System.Windows.Forms.Application.EnableVisualStyles();
            using var tray = app.Services.GetRequiredService<TrayApplicationContext>();
            tray.Initialize(showEvent, openDashboardOnStart: !background);
            System.Windows.Forms.Application.Run(tray);

            app.StopAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult();
            (app as IDisposable)?.Dispose();
            return 0;
        }
        catch (Exception ex)
        {
            ErrorLog.Write("Fatal startup/runtime error", ex);
            System.Windows.Forms.MessageBox.Show(
                $"Early Access Tracker failed to start:\n\n{ex.Message}\n\nDetails: {AppPaths.ErrorLogPath}",
                "Early Access Tracker", System.Windows.Forms.MessageBoxButtons.OK, System.Windows.Forms.MessageBoxIcon.Error);
            return 1;
        }
    }

    private static WebApplication BuildWebApp(string[] args)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            Args = args,
            // The scheduled task starts us in C:\Windows\System32; resolve content relative to the exe.
            ContentRootPath = AppContext.BaseDirectory,
        });

        // Secrets are loaded in every environment (this is a single-user local app, and the
        // scheduled task runs in Production).
        builder.Configuration.AddUserSecrets(typeof(Program).Assembly, optional: true, reloadOnChange: true);

        // Fail fast on DI mistakes in every environment, not just Development.
        builder.Host.UseDefaultServiceProvider(o => { o.ValidateOnBuild = true; o.ValidateScopes = true; });

        // Serve CSS/JS from the build output when running un-published (no-op once published).
        builder.WebHost.UseStaticWebAssets();

        var port = builder.Configuration.GetValue("Dashboard:Port", 5179);
        builder.WebHost.UseUrls($"http://127.0.0.1:{port}");

        var services = builder.Services;
        services.AddSingleton(TimeProvider.System);
        services.Configure<SteamOptions>(builder.Configuration.GetSection(SteamOptions.Section));
        services.Configure<SyncOptions>(builder.Configuration.GetSection(SyncOptions.Section));

        var connection = $"Data Source={AppPaths.DatabasePath}";
        services.AddDbContext<TrackerDbContext>(o => o.UseSqlite(connection));
        services.AddDbContextFactory<TrackerDbContext>(o => o.UseSqlite(connection), ServiceLifetime.Scoped);

        services.AddHttpClient<ISteamApiClient, SteamApiClient>(c =>
        {
            c.Timeout = TimeSpan.FromSeconds(30);
            c.DefaultRequestHeaders.UserAgent.ParseAdd("EarlyAccessTracker/1.0");
        });

        services.AddScoped<SyncEngine>();
        services.AddSingleton<SyncCoordinator>();
        services.AddSingleton<TrayApplicationContext>();
        services.AddHostedService<DailySyncScheduler>();

        services.AddRazorComponents().AddInteractiveServerComponents();

        var app = builder.Build();
        app.UseExceptionHandler("/Error", createScopeForErrors: true);
        app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
        app.UseAntiforgery();
        app.MapStaticAssets();
        app.MapRazorComponents<Components.App>().AddInteractiveServerRenderMode();
        return app;
    }
}
