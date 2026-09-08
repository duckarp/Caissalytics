using Caissalytics.Components;
using Caissalytics.Data;
using Caissalytics.Engine;
using Caissalytics.Localization;
using Microsoft.Extensions.DependencyInjection;
using Photino.Blazor;

var appBuilder = PhotinoBlazorAppBuilder.CreateDefault(args);

// Register application services
appBuilder.Services.AddLogging();
appBuilder.Services.AddHttpClient();
appBuilder.Services.AddSingleton<IEngineService, EngineManager>();
appBuilder.Services.AddSingleton<IDatabaseService, DatabaseManager>();
appBuilder.Services.AddSingleton<IGameAnalysisService, GameAnalysisService>();
appBuilder.Services.AddSingleton<IOnlineGameSyncService, OnlineGameSyncService>();
appBuilder.Services.AddSingleton<IUserProfileService, UserProfileService>();
appBuilder.Services.AddSingleton<IUserAnalyticsService, UserAnalyticsService>();
appBuilder.Services.AddSingleton<IUpdateService, UpdateService>();
appBuilder.Services.AddSingleton<IPuzzleService, PuzzleService>();
appBuilder.Services.AddSingleton<IRepertoireService, RepertoireService>();
appBuilder.Services.AddSingleton<IHomeworkService, HomeworkService>();
appBuilder.Services.AddScoped<IAppearanceService, AppearanceService>();
appBuilder.Services.AddScoped<ILocalizationService, LocalizationService>();
appBuilder.Services.AddSingleton<IOpponentDossierService, OpponentDossierService>();
appBuilder.Services.AddSingleton<IFideScoutingService, FideScoutingService>();
appBuilder.Services.AddSingleton<IChessResultsScoutingService, ChessResultsScoutingService>();
appBuilder.Services.AddSingleton<ITablebaseService, TablebaseService>();
appBuilder.Services.AddSingleton<LichessExplorerClient>();
appBuilder.Services.AddScoped<WorkspaceState>();

// Register root desktop component
appBuilder.RootComponents.Add<App>("div#app");

var app = appBuilder.Build();

// Trigger non-blocking update check on launch
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
	catch { }
});

// Configure the native desktop window
app.MainWindow
	.SetTitle("Caissalytics")
	.SetSize(1400, 900)
	.SetMinSize(1000, 650)
	.SetMediaAutoplayEnabled(true)
	.SetUseOsDefaultLocation(false);

AppDomain.CurrentDomain.UnhandledException += (sender, error) =>
{
	Console.Error.WriteLine($"[Caissalytics] Unhandled exception: {error.ExceptionObject}");
};

app.Run();