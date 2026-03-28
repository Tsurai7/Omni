using System.Diagnostics;
using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Http.Resilience;
using Omni.Client.Abstractions;
using Omni.Client.Core.Abstractions.Api;
using Omni.Client.Infrastructure.Http;
using Omni.Client.Presentation.ViewModels;
using Omni.Client.Services;
using Polly;
using Polly.Timeout;
using Refit;

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

        var jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            PropertyNameCaseInsensitive = true
        };
        services.AddSingleton(jsonOptions);

        var refitSettings = new RefitSettings
        {
            ContentSerializer = new SystemTextJsonContentSerializer(jsonOptions)
        };

        services.AddSingleton<IThemeService, ThemeService>();
        services.AddSingleton<ITokenStorage, MauiTokenStorage>();
        services.AddTransient<AuthenticatedHttpHandler>();
        services.AddTransient<UnauthorizedHttpHandler>();

        services.AddHttpClient("OmniAuth", (sp, client) =>
        {
            var opts = sp.GetRequiredService<BackendOptions>();
            var baseUri = BuildGatewayBaseUri(opts.BaseUrl);
            if (baseUri != null)
                client.BaseAddress = baseUri;
            client.Timeout = TimeSpan.FromSeconds(30);
        });

        services.AddHttpClient("OmniBackend", (sp, client) =>
        {
            var opts = sp.GetRequiredService<BackendOptions>();
            var baseUri = BuildGatewayBaseUri(opts.BaseUrl);
            if (baseUri != null)
                client.BaseAddress = baseUri;
        })
        .AddHttpMessageHandler<AuthenticatedHttpHandler>()
        .AddHttpMessageHandler<UnauthorizedHttpHandler>()
        .AddResilienceHandler("omni-pipeline", pipeline =>
        {
            pipeline.AddTimeout(TimeSpan.FromSeconds(25));

            pipeline.AddRetry(new HttpRetryStrategyOptions
            {
                MaxRetryAttempts = 3,
                Delay = TimeSpan.FromMilliseconds(500),
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,
                ShouldHandle = new PredicateBuilder<HttpResponseMessage>()
                    .Handle<HttpRequestException>()
                    .Handle<TimeoutRejectedException>()
                    .HandleResult(r => r.StatusCode is
                        HttpStatusCode.RequestTimeout or
                        HttpStatusCode.TooManyRequests or
                        >= HttpStatusCode.InternalServerError)
            });

            pipeline.AddCircuitBreaker(new HttpCircuitBreakerStrategyOptions
            {
                SamplingDuration = TimeSpan.FromSeconds(30),
                MinimumThroughput = 5,
                FailureRatio = 0.5,
                BreakDuration = TimeSpan.FromSeconds(15),
                OnOpened = args =>
                {
                    Debug.WriteLine($"[Circuit] Opened for {args.BreakDuration.TotalSeconds}s");
                    return ValueTask.CompletedTask;
                }
            });

            pipeline.AddTimeout(TimeSpan.FromSeconds(30));
        });

        services.AddHttpClient("OmniBackendStream", (sp, client) =>
        {
            var opts = sp.GetRequiredService<BackendOptions>();
            var baseUri = BuildGatewayBaseUri(opts.BaseUrl);
            if (baseUri != null)
                client.BaseAddress = baseUri;
            client.Timeout = System.Threading.Timeout.InfiniteTimeSpan;
        })
        .AddHttpMessageHandler<AuthenticatedHttpHandler>()
        .AddHttpMessageHandler<UnauthorizedHttpHandler>();

        services.AddRefitClient<ITaskApi>(refitSettings)
            .ConfigureHttpClient((sp, c) =>
            {
                var opts = sp.GetRequiredService<BackendOptions>();
                var baseUri = BuildGatewayBaseUri(opts.BaseUrl);
                if (baseUri != null) c.BaseAddress = baseUri;
            })
            .AddHttpMessageHandler<AuthenticatedHttpHandler>()
            .AddHttpMessageHandler<UnauthorizedHttpHandler>()
            .AddResilienceHandler("omni-pipeline", ConfigureDefaultPipeline);

        services.AddRefitClient<ISessionApi>(refitSettings)
            .ConfigureHttpClient((sp, c) =>
            {
                var opts = sp.GetRequiredService<BackendOptions>();
                var baseUri = BuildGatewayBaseUri(opts.BaseUrl);
                if (baseUri != null) c.BaseAddress = baseUri;
            })
            .AddHttpMessageHandler<AuthenticatedHttpHandler>()
            .AddHttpMessageHandler<UnauthorizedHttpHandler>()
            .AddResilienceHandler("omni-pipeline", ConfigureDefaultPipeline);

        services.AddRefitClient<IUsageApi>(refitSettings)
            .ConfigureHttpClient((sp, c) =>
            {
                var opts = sp.GetRequiredService<BackendOptions>();
                var baseUri = BuildGatewayBaseUri(opts.BaseUrl);
                if (baseUri != null) c.BaseAddress = baseUri;
            })
            .AddHttpMessageHandler<AuthenticatedHttpHandler>()
            .AddHttpMessageHandler<UnauthorizedHttpHandler>()
            .AddResilienceHandler("omni-pipeline", ConfigureDefaultPipeline);

        services.AddRefitClient<IProductivityApi>(refitSettings)
            .ConfigureHttpClient((sp, c) =>
            {
                var opts = sp.GetRequiredService<BackendOptions>();
                var baseUri = BuildGatewayBaseUri(opts.BaseUrl);
                if (baseUri != null) c.BaseAddress = baseUri;
            })
            .AddHttpMessageHandler<AuthenticatedHttpHandler>()
            .AddHttpMessageHandler<UnauthorizedHttpHandler>()
            .AddResilienceHandler("omni-pipeline", ConfigureDefaultPipeline);

        services.AddRefitClient<ICalendarApi>(refitSettings)
            .ConfigureHttpClient((sp, c) =>
            {
                var opts = sp.GetRequiredService<BackendOptions>();
                var baseUri = BuildGatewayBaseUri(opts.BaseUrl);
                if (baseUri != null) c.BaseAddress = baseUri;
            })
            .AddHttpMessageHandler<AuthenticatedHttpHandler>()
            .AddHttpMessageHandler<UnauthorizedHttpHandler>()
            .AddResilienceHandler("omni-pipeline", ConfigureDefaultPipeline);

        services.AddRefitClient<IAiApi>(refitSettings)
            .ConfigureHttpClient((sp, c) =>
            {
                var opts = sp.GetRequiredService<BackendOptions>();
                var baseUri = BuildGatewayBaseUri(opts.BaseUrl);
                if (baseUri != null) c.BaseAddress = baseUri;
            })
            .AddHttpMessageHandler<AuthenticatedHttpHandler>()
            .AddHttpMessageHandler<UnauthorizedHttpHandler>()
            .AddResilienceHandler("omni-pipeline", ConfigureDefaultPipeline);

        services.AddSingleton<IAuthService>(sp =>
            new AuthService(
                sp.GetRequiredService<IHttpClientFactory>().CreateClient("OmniAuth"),
                sp.GetRequiredService<JsonSerializerOptions>(),
                sp.GetRequiredService<ITokenStorage>()));

        services.AddSingleton<IUsageService>(sp =>
            new UsageService(
                sp.GetRequiredService<IUsageApi>(),
                sp.GetRequiredService<IActiveWindowTracker>(),
                sp.GetRequiredService<LocalDatabaseService>()));

        services.AddSingleton<ISessionService>(sp =>
            new SessionService(
                sp.GetRequiredService<ISessionApi>(),
                sp.GetRequiredService<LocalDatabaseService>()));

        services.AddSingleton<ITaskService>(sp =>
            new TaskService(
                sp.GetRequiredService<ITaskApi>(),
                sp.GetRequiredService<LocalDatabaseService>()));

        services.AddSingleton<IProductivityService>(sp =>
            new ProductivityService(
                sp.GetRequiredService<IProductivityApi>()));

        services.AddSingleton<IFocusScoreService>(sp =>
            new FocusScoreService(
                sp.GetRequiredService<IAiApi>(),
                sp.GetRequiredService<IAuthService>()));

        services.AddSingleton<IChatService>(sp =>
            new ChatService(
                sp.GetRequiredService<IHttpClientFactory>().CreateClient("OmniBackendStream"),
                sp.GetRequiredService<IAiApi>(),
                sp.GetRequiredService<IAuthService>(),
                sp.GetRequiredService<JsonSerializerOptions>()));

        services.AddSingleton<CalendarService>(sp =>
            new CalendarService(
                sp.GetRequiredService<ICalendarApi>()));

        services.AddSingleton(_ => new LocalDatabaseService(
            Path.Combine(FileSystem.AppDataDirectory, "omni_local.db")));

        services.AddSingleton<ISyncService>(sp =>
            new SyncService(
                sp.GetRequiredService<ITaskApi>(),
                sp.GetRequiredService<IUsageApi>(),
                sp.GetRequiredService<ISessionApi>(),
                sp.GetRequiredService<LocalDatabaseService>(),
                sp.GetRequiredService<JsonSerializerOptions>()));

        services.AddActiveWindowTracker();
        services.AddNotificationManager();
        services.AddSingleton<DistractionConfig>(_ => new DistractionConfig());
        services.AddSingleton<ISessionDistractionService>(sp =>
            new SessionDistractionService(
                sp.GetRequiredService<IActiveWindowTracker>(),
                sp.GetRequiredService<INotificationManager>(),
                sp.GetRequiredService<DistractionConfig>()));
        services.AddSingleton<IRunningSessionState, RunningSessionStateService>();

        services.AddTransient<LoginViewModel>();
        services.AddTransient<RegisterViewModel>();

        // Flyout pages: singleton so they stay alive between navigations,
        // preserving state and avoiding reconstruction cost on each switch.
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<ChatViewModel>();
        services.AddSingleton<SessionViewModel>();
        services.AddSingleton<TasksViewModel>();
        services.AddSingleton<UsageStatsViewModel>();
        services.AddSingleton<CalendarViewModel>();
        services.AddSingleton<AccountViewModel>();

        services.AddSingleton<MainPage>();
        services.AddSingleton<UsageStatsPage>();
        services.AddSingleton<SessionPage>();
        services.AddSingleton<TasksPage>();
        services.AddSingleton<AccountPage>();
        services.AddSingleton<ChatPage>();
        services.AddSingleton<CalendarPage>();

        // Navigation-push pages: transient (created fresh each time).
        services.AddTransient<DigestPage>();

        return services;
    }

    private static void ConfigureDefaultPipeline(ResiliencePipelineBuilder<HttpResponseMessage> pipeline)
    {
        pipeline.AddTimeout(TimeSpan.FromSeconds(25));

        pipeline.AddRetry(new HttpRetryStrategyOptions
        {
            MaxRetryAttempts = 3,
            Delay = TimeSpan.FromMilliseconds(500),
            BackoffType = DelayBackoffType.Exponential,
            UseJitter = true,
            ShouldHandle = new PredicateBuilder<HttpResponseMessage>()
                .Handle<HttpRequestException>()
                .Handle<TimeoutRejectedException>()
                .HandleResult(r => r.StatusCode is
                    HttpStatusCode.RequestTimeout or
                    HttpStatusCode.TooManyRequests or
                    >= HttpStatusCode.InternalServerError)
        });

        pipeline.AddCircuitBreaker(new HttpCircuitBreakerStrategyOptions
        {
            SamplingDuration = TimeSpan.FromSeconds(30),
            MinimumThroughput = 5,
            FailureRatio = 0.5,
            BreakDuration = TimeSpan.FromSeconds(15),
            OnOpened = args =>
            {
                Debug.WriteLine($"[Circuit] Opened for {args.BreakDuration.TotalSeconds}s");
                return ValueTask.CompletedTask;
            }
        });

        pipeline.AddTimeout(TimeSpan.FromSeconds(30));
    }

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
        services.AddSingleton<IActiveWindowTracker, ActiveWindowTrackerMacOs>();
#else
        services.AddSingleton<IActiveWindowTracker, ActiveWindowTrackerStub>();
#endif
        return services;
    }
}
