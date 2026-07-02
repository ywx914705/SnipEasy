using Microsoft.Extensions.DependencyInjection;
using SnipEasy.App.ViewModels;

namespace SnipEasy.App.Services;

/// <summary>
/// Extension methods for configuring dependency injection services.
/// </summary>
internal static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers all application services with the dependency injection container.
    /// </summary>
    public static IServiceCollection AddSnipEasyServices(this IServiceCollection services)
    {
        // Core infrastructure - use factory for AppPaths since it has static Create()
        services.AddSingleton(provider => AppPaths.Create());

        // AppLogger needs the log path from AppPaths
        services.AddSingleton(provider =>
        {
            var paths = provider.GetRequiredService<AppPaths>();
            return new AppLogger(paths.LogPath);
        });

        // AppSettings - load from settings file
        services.AddSingleton(provider =>
        {
            var settingsService = provider.GetRequiredService<AppSettingsService>();
            return settingsService.Load();
        });

        // Data services
        services.AddSingleton<AppSettingsService>();

        // CaptureHistoryService needs history path from AppPaths
        services.AddSingleton(provider =>
        {
            var paths = provider.GetRequiredService<AppPaths>();
            var logger = provider.GetRequiredService<AppLogger>();
            return new CaptureHistoryService(paths.HistoryPath, logger);
        });

        services.AddSingleton<RecordingDraftService>();
        services.AddSingleton<DiagnosticPackageService>();

        // Capture services
        services.AddSingleton<ScreenCaptureService>();
        services.AddSingleton<ClipboardService>();

        // Recording services
        services.AddSingleton<IRecordingService, RecordingServiceCoordinator>();

        // UI services
        services.AddSingleton<HotkeyManager>();
        services.AddTransient<TrayService>();
        services.AddSingleton<StickerManager>();

        // ViewModels
        services.AddTransient<SettingsViewModel>();
        services.AddTransient<RecordingViewModel>();
        services.AddTransient<HistoryViewModel>();
        services.AddTransient<MainViewModel>();

        // Windows
        services.AddTransient<MainWindow>();

        return services;
    }

    /// <summary>
    /// Creates and configures the application service provider.
    /// </summary>
    public static ServiceProvider BuildSnipEasyServiceProvider()
    {
        var services = new ServiceCollection();
        services.AddSnipEasyServices();
        return services.BuildServiceProvider();
    }
}
