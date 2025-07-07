using Omni.Client.Abstractions;

namespace Omni.Client;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection ConfigureServices(this IServiceCollection services)
    {
        services.AddTransient<MainPage>(); ;
        
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