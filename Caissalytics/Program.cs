using System.Runtime.InteropServices;
using Caissalytics.Components;
using Caissalytics.Data;
using Caissalytics.Engine;
using Caissalytics.Localization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Photino.Blazor;

namespace Caissalytics;

internal class Program
{
    [DllImport("libglib-2.0.so.0", EntryPoint = "g_set_prgname", CallingConvention = CallingConvention.Cdecl)]
    private static extern void g_set_prgname(string prgname);

    [STAThread]
    static void Main(string[] args)
    {
        SetLinuxProgramName("caissalytics");

        var appBuilder = PhotinoBlazorAppBuilder.CreateDefault(args);

        // Register application services
        appBuilder.Services.AddLogging();
        appBuilder.Services.AddHttpClient();
        appBuilder.Services.AddSingleton<IEngineService, EngineManager>();
        appBuilder.Services.AddSingleton<IMaiaModelService, MaiaModelService>();
        appBuilder.Services.AddSingleton<IPracticeEngineService, PracticeEngineService>();
        appBuilder.Services.AddSingleton<IDatabaseService, DatabaseManager>();
        appBuilder.Services.AddSingleton<IGameAnalysisService, GameAnalysisService>();
        appBuilder.Services.AddSingleton<IOnlineGameSyncService, OnlineGameSyncService>();
        appBuilder.Services.AddSingleton<IUserProfileService, UserProfileService>();
        appBuilder.Services.AddSingleton<IUserAnalyticsService, UserAnalyticsService>();
        appBuilder.Services.AddSingleton<IUpdateService, UpdateService>();
        appBuilder.Services.AddSingleton<IPuzzleService, PuzzleService>();
        appBuilder.Services.AddSingleton<IRepertoireService, RepertoireService>();
        appBuilder.Services.AddSingleton<IChessClubService, ChessClubService>();
        appBuilder.Services.AddSingleton<ISkppIntegrationService, SkppIntegrationService>();
        appBuilder.Services.AddSingleton<IAppWindowProvider, AppWindowProvider>();
        appBuilder.Services.AddSingleton<IHomeworkService, HomeworkService>();
        appBuilder.Services.AddScoped<IAppearanceService, AppearanceService>();
        appBuilder.Services.AddScoped<ILocalizationService, LocalizationService>();
        appBuilder.Services.AddSingleton<IOpponentDossierService, OpponentDossierService>();
        appBuilder.Services.AddSingleton<IFideScoutingService, FideScoutingService>();
        appBuilder.Services.AddSingleton<IChessResultsScoutingService, ChessResultsScoutingService>();
        appBuilder.Services.AddSingleton<ITablebaseService, TablebaseService>();
        appBuilder.Services.AddSingleton<LichessExplorerClient>();
        appBuilder.Services.AddScoped<WorkspaceState>();

        // Configure file provider supporting both physical wwwroot and embedded resources for single-file binaries
        var physicalWwwroot = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "wwwroot");
        IFileProvider fileProvider;
        try
        {
            var embeddedProvider = new ManifestEmbeddedFileProvider(typeof(App).Assembly, "wwwroot");
            if (Directory.Exists(physicalWwwroot))
            {
                fileProvider = new CompositeFileProvider(
                    new PhysicalFileProvider(physicalWwwroot),
                    embeddedProvider);
            }
            else
            {
                fileProvider = embeddedProvider;
            }
        }
        catch
        {
            if (!Directory.Exists(physicalWwwroot))
            {
                Directory.CreateDirectory(physicalWwwroot);
            }
            fileProvider = new PhysicalFileProvider(physicalWwwroot);
        }

        var existingProviderDescriptor = appBuilder.Services.FirstOrDefault(s => s.ServiceType == typeof(IFileProvider));
        if (existingProviderDescriptor != null)
        {
            appBuilder.Services.Remove(existingProviderDescriptor);
        }
        appBuilder.Services.AddSingleton<IFileProvider>(fileProvider);

        // Register root desktop component
        appBuilder.RootComponents.Add<App>("#app");

        var app = appBuilder.Build();

        // Expose the native window (created during Build) to Blazor components
        app.Services.GetRequiredService<IAppWindowProvider>().Window = app.MainWindow;

        // Trigger non-blocking update check on launch (runs safely on ThreadPool)
        var updateService = app.Services.GetRequiredService<IUpdateService>();
        _ = Task.Run(async () =>
        {
            try
            {
                var settings = await updateService.GetSettingsAsync();
                if (settings.AutoCheckOnStartup)
                {
                    await Task.Delay(2500);
                    await updateService.CheckForUpdatesAsync();
                }
            }
            catch
            {
                // Silently ignore background update check failures
            }
        });

        // Configure the native desktop window
        var iconFile = EnsureIconOnDisk(fileProvider);

        app.MainWindow
            .SetTitle("Caissalytics")
            .SetSize(1400, 900)
            .SetMinSize(1000, 650)
            .SetMediaAutoplayEnabled(true)
            .SetUseOsDefaultLocation(false);

        if (!string.IsNullOrEmpty(iconFile) && File.Exists(iconFile))
        {
            try
            {
                app.MainWindow.SetIconFile(iconFile);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[Caissalytics] Could not set window icon: {ex.Message}");
            }
        }

        AppDomain.CurrentDomain.UnhandledException += (sender, error) =>
        {
            Console.Error.WriteLine($"[Caissalytics] Unhandled exception: {error.ExceptionObject}");
        };

        // Complete Linux desktop integration after fileProvider is ready
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            try
            {
                EnsureLinuxDesktopIntegration(fileProvider);
            }
            catch
            {
                // Non-fatal
            }
        }

        app.Run();
    }

    private static void SetLinuxProgramName(string name)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            try
            {
                g_set_prgname(name);
            }
            catch
            {
                // Non-fatal if libglib is not available
            }
        }
    }

    private static string? EnsureIconOnDisk(IFileProvider fileProvider)
    {
        try
        {
            // 1. Check physical file paths next to binary or current working directory
            string[] physicalCandidates = [
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "wwwroot", "icon-256.png"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "wwwroot", "icon.png"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "wwwroot", "favicon.png"),
                Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "icon-256.png"),
                Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "favicon.png"),
            ];

            foreach (var c in physicalCandidates)
            {
                if (File.Exists(c) && new FileInfo(c).Length > 0)
                {
                    return c;
                }
            }

            // 2. Extract embedded icon into persistent application data or temp directory
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var targetDir = !string.IsNullOrEmpty(appData)
                ? Path.Combine(appData, "Caissalytics")
                : Path.Combine(Path.GetTempPath(), "caissalytics");

            Directory.CreateDirectory(targetDir);
            var targetPng = Path.Combine(targetDir, "icon-256.png");

            if (File.Exists(targetPng) && new FileInfo(targetPng).Length > 0)
            {
                return targetPng;
            }

            string[] embeddedNames = ["icon-256.png", "icon.png", "favicon.png"];
            foreach (var name in embeddedNames)
            {
                var fileInfo = fileProvider.GetFileInfo(name);
                if (fileInfo.Exists)
                {
                    using var src = fileInfo.CreateReadStream();
                    using var dst = File.Create(targetPng);
                    src.CopyTo(dst);
                    return targetPng;
                }
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[Caissalytics] Could not extract icon to disk: {ex.Message}");
        }

        return null;
    }

    private static void EnsureLinuxDesktopIntegration(IFileProvider fileProvider)
    {
        try
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (string.IsNullOrEmpty(home)) return;

            var appsDir = Path.Combine(home, ".local", "share", "applications");
            var icon256Dir = Path.Combine(home, ".local", "share", "icons", "hicolor", "256x256", "apps");
            var iconSvgDir = Path.Combine(home, ".local", "share", "icons", "hicolor", "scalable", "apps");

            Directory.CreateDirectory(appsDir);
            Directory.CreateDirectory(icon256Dir);
            Directory.CreateDirectory(iconSvgDir);

            // Copy/extract 256x256 icon
            var targetPng = Path.Combine(icon256Dir, "caissalytics.png");
            if (!File.Exists(targetPng) || new FileInfo(targetPng).Length == 0)
            {
                var pngInfo = fileProvider.GetFileInfo("icon-256.png");
                if (!pngInfo.Exists) pngInfo = fileProvider.GetFileInfo("icon.png");
                if (!pngInfo.Exists) pngInfo = fileProvider.GetFileInfo("favicon.png");

                if (pngInfo.Exists)
                {
                    using var src = pngInfo.CreateReadStream();
                    using var dst = File.Create(targetPng);
                    src.CopyTo(dst);
                    File.Copy(targetPng, Path.Combine(icon256Dir, "Caissalytics.png"), true);
                }
            }

            // Copy/extract scalable SVG icon
            var targetSvg = Path.Combine(iconSvgDir, "caissalytics.svg");
            if (!File.Exists(targetSvg) || new FileInfo(targetSvg).Length == 0)
            {
                var svgInfo = fileProvider.GetFileInfo("images/icon.svg");
                if (!svgInfo.Exists) svgInfo = fileProvider.GetFileInfo("icon.svg");
                if (!svgInfo.Exists) svgInfo = fileProvider.GetFileInfo("favicon.svg");

                if (svgInfo.Exists)
                {
                    using var src = svgInfo.CreateReadStream();
                    using var dst = File.Create(targetSvg);
                    src.CopyTo(dst);
                    File.Copy(targetSvg, Path.Combine(iconSvgDir, "Caissalytics.svg"), true);
                }
            }

            var desktopFile = Path.Combine(appsDir, "caissalytics.desktop");
            var exePath = Environment.ProcessPath ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Caissalytics");
            var desktopContent = $"""
                [Desktop Entry]
                Name=Caissalytics
                GenericName=Chess Analysis & Database
                Comment=Chess Insights & Performance
                Exec="{exePath}"
                Icon=caissalytics
                Terminal=false
                Type=Application
                Categories=Game;BoardGame;Utility;
                StartupWMClass=caissalytics
                StartupNotify=true
                """;
            File.WriteAllText(desktopFile, desktopContent);
            File.WriteAllText(Path.Combine(appsDir, "Caissalytics.desktop"), desktopContent);
        }
        catch
        {
            // Non-fatal desktop entry creation
        }
    }
}