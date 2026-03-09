using System.Text.Json;
using Omni.Client.Abstractions;
using Omni.Client.Services;

namespace Omni.Client;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection ConfigureServices(this IServiceCollection services)
    {
        services.AddSingleton(_ => new BackendOptions { BaseUrl = "http://localhost:8080" });

        services.AddSingleton(_ => new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            PropertyNameCaseInsensitive = true
        });

        services.AddHttpClient("OmniBackend", (sp, client) =>
        {
            var opts = sp.GetRequiredService<BackendOptions>();
            client.BaseAddress = new Uri(opts.BaseUrl.TrimEnd('/') + "/");
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
                sp.GetRequiredService<JsonSerializerOptions>()));

        services.AddSingleton<ISessionService>(sp =>
            new SessionService(
                sp.GetRequiredService<IHttpClientFactory>().CreateClient("OmniBackend"),
                sp.GetRequiredService<IAuthService>(),
                sp.GetRequiredService<JsonSerializerOptions>()));

        services.AddTransient<MainPage>();
        services.AddTransient<UsageStatsPage>();
        services.AddTransient<SessionPage>();
        services.AddTransient<AccountPage>();

        services.AddActiveWindowTracker();
        return services;
    }

    private static IServiceCollection AddActiveWindowTracker(this IServiceCollection services)
    {
        
#if WINDOWS
        services.AddSingleton<IActiveWindowTracker, ActiveWindowTrackerWindows>();
#elif MACCATALYST
        services.AddSingleton<IActiveWindowTracker, ActiveWindowTrackerMacOS>();
#endif

        return services;
    }
}