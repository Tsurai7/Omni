using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Omni.Client.Abstractions;
using Omni.Client.Services;

namespace Omni.Client;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection ConfigureServices(this IServiceCollection services)
    {
        services.AddSingleton(sp =>
        {
            var config = sp.GetRequiredService<IConfiguration>();
            var opts = new BackendOptions();
            config.GetSection(BackendOptions.SectionName).Bind(opts);
            return opts;
        });

        services.AddSingleton(_ => new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            PropertyNameCaseInsensitive = true
        });

        services.AddHttpClient("OmniBackend", (sp, client) =>
        {
            var opts = sp.GetRequiredService<BackendOptions>();
            var baseUri = BuildGatewayBaseUri(opts.BaseUrl);
            if (baseUri != null)
                client.BaseAddress = baseUri;
            client.Timeout = TimeSpan.FromSeconds(30);
        });
        services.AddSingleton<IAuthService>(sp =>
            new AuthService(
                sp.GetRequiredService<IHttpClientFactory>().CreateClient("OmniBackend"),
                sp.GetRequiredService<JsonSerializerOptions>()));
        services.AddSingleton<IUsageService>(sp =>
            new UsageService(
                sp.GetRequiredService<IHttpClientFactory>().CreateClient("OmniBackend"),
                sp.GetRequiredService<IAuthService>(),
                sp.GetRequiredService<IActiveWindowTracker>(),
                sp.GetRequiredService<JsonSerializerOptions>(),
                sp.GetRequiredService<LocalDatabaseService>()));

        services.AddSingleton<ISessionService>(sp =>
            new SessionService(
                sp.GetRequiredService<IHttpClientFactory>().CreateClient("OmniBackend"),
                sp.GetRequiredService<IAuthService>(),
                sp.GetRequiredService<JsonSerializerOptions>(),
                sp.GetRequiredService<LocalDatabaseService>()));

        services.AddSingleton<ITaskService>(sp =>
            new TaskService(
                sp.GetRequiredService<IHttpClientFactory>().CreateClient("OmniBackend"),
                sp.GetRequiredService<IAuthService>(),
                sp.GetRequiredService<JsonSerializerOptions>(),
                sp.GetRequiredService<LocalDatabaseService>()));

        services.AddSingleton<IProductivityService>(sp =>
            new ProductivityService(
                sp.GetRequiredService<IHttpClientFactory>().CreateClient("OmniBackend"),
                sp.GetRequiredService<IAuthService>(),
                sp.GetRequiredService<JsonSerializerOptions>()));

        services.AddSingleton<IFocusScoreService>(sp =>
            new FocusScoreService(
                sp.GetRequiredService<IHttpClientFactory>().CreateClient("OmniBackend"),
                sp.GetRequiredService<IAuthService>(),
                sp.GetRequiredService<JsonSerializerOptions>()));

        // Chat service uses a dedicated HttpClient with no timeout so SSE streams
        // are not cut off by the 30s default.
        services.AddHttpClient("OmniBackendStream", (sp, client) =>
        {
            var opts = sp.GetRequiredService<BackendOptions>();
            var baseUri = BuildGatewayBaseUri(opts.BaseUrl);
            if (baseUri != null)
                client.BaseAddress = baseUri;
            client.Timeout = System.Threading.Timeout.InfiniteTimeSpan;
        });
        services.AddSingleton<IChatService>(sp =>
            new ChatService(
                sp.GetRequiredService<IHttpClientFactory>().CreateClient("OmniBackendStream"),
                sp.GetRequiredService<IAuthService>(),
                sp.GetRequiredService<JsonSerializerOptions>()));

        services.AddSingleton<CalendarService>(sp =>
            new CalendarService(
                sp.GetRequiredService<IHttpClientFactory>().CreateClient("OmniBackend"),
                sp.GetRequiredService<IAuthService>(),
                sp.GetRequiredService<JsonSerializerOptions>()));

        services.AddTransient<MainPage>();
        services.AddTransient<UsageStatsPage>();
        services.AddTransient<SessionPage>();
        services.AddTransient<TasksPage>();
        services.AddTransient<AccountPage>();
        services.AddTransient<DigestPage>();
        services.AddTransient<ChatPage>();
        services.AddTransient<CalendarPage>();

        services.AddActiveWindowTracker();
        services.AddNotificationManager();
        services.AddSingleton<DistractionConfig>(_ => new DistractionConfig());
        services.AddSingleton<ISessionDistractionService>(sp =>
            new SessionDistractionService(
                sp.GetRequiredService<IActiveWindowTracker>(),
                sp.GetRequiredService<INotificationManager>(),
                sp.GetRequiredService<DistractionConfig>()));
        services.AddSingleton<IRunningSessionState, RunningSessionStateService>();

        services.AddSingleton<LocalDatabaseService>();
        services.AddSingleton<ISyncService>(sp =>
            new SyncService(
                sp.GetRequiredService<IHttpClientFactory>().CreateClient("OmniBackend"),
                sp.GetRequiredService<IAuthService>(),
                sp.GetRequiredService<LocalDatabaseService>(),
                sp.GetRequiredService<JsonSerializerOptions>()));

        return services;
    }

    // Gateway root only (e.g. http://127.0.0.1:8080); strip trailing /api so requests are not /api/api/...
    private static Uri? BuildGatewayBaseUri(string? rawBaseUrl)
    {
        if (string.IsNullOrWhiteSpace(rawBaseUrl))
            return null;
        var s = rawBaseUrl.Trim();
        while (s.EndsWith('/'))
            s = s[..^1];
        while (s.EndsWith("/api", StringComparison.OrdinalIgnoreCase))
        {
            s = s[..^4];
            while (s.EndsWith('/'))
                s = s[..^1];
        }

        return string.IsNullOrWhiteSpace(s) ? null : new Uri(s + "/");
    }

    private static IServiceCollection AddNotificationManager(this IServiceCollection services)
    {
#if MACCATALYST
        services.AddSingleton<INotificationManager, Platforms.MacCatalyst.NotificationManagerService>();
#elif WINDOWS
        services.AddSingleton<INotificationManager, Platforms.Windows.NotificationManagerService>();
#else
        services.AddSingleton<INotificationManager, NotificationManagerStub>();
#endif
        return services;
    }

    private static IServiceCollection AddActiveWindowTracker(this IServiceCollection services)
    {
#if WINDOWS
        services.AddSingleton<IActiveWindowTracker, ActiveWindowTrackerWindows>();
#elif MACCATALYST
        services.AddSingleton<IActiveWindowTracker, ActiveWindowTrackerMacOS>();
#else
        services.AddSingleton<IActiveWindowTracker, ActiveWindowTrackerStub>();
#endif
        return services;
    }
}