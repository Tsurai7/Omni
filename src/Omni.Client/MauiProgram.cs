using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Omni.Client;

public static class MauiProgram
{
    public static IServiceProvider? AppServices { get; private set; }

    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();

        var assembly = Assembly.GetExecutingAssembly();
        var env =
#if DEBUG
            "Development";
#else
            "Production";
#endif
        var configBuilder = new ConfigurationBuilder()
            .AddJsonStream(GetEmbeddedStream(assembly, "Config.appsettings.json"));
        var envStream = GetEmbeddedStreamOrNull(assembly, $"Config.appsettings.{env}.json");
        if (envStream != null)
            configBuilder.AddJsonStream(envStream);
        var config = configBuilder.Build();
        builder.Configuration.AddConfiguration(config);
        builder.Services.AddSingleton<IConfiguration>(config);

        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
                fonts.AddFont("Inter-Regular.ttf", "InterRegular");
                fonts.AddFont("Inter-Medium.ttf", "InterMedium");
                fonts.AddFont("Inter-SemiBold.ttf", "InterSemiBold");
                fonts.AddFont("Inter-Bold.ttf", "InterBold");
            });

        builder.Services.ConfigureServices();

#if DEBUG
        builder.Logging.AddDebug();
#endif

        var app = builder.Build();
        AppServices = app.Services;
        return app;
    }

    private static Stream GetEmbeddedStream(Assembly assembly, string relativePath)
    {
        var stream = GetEmbeddedStreamOrNull(assembly, relativePath);
        if (stream == null)
            throw new InvalidOperationException($"Embedded resource not found: {assembly.GetName().Name}.{relativePath}. Ensure Config files are set to EmbeddedResource.");
        return stream;
    }

    private static Stream? GetEmbeddedStreamOrNull(Assembly assembly, string relativePath)
    {
        var name = $"{assembly.GetName().Name}.{relativePath}".Replace('\\', '.').Replace('/', '.');
        return assembly.GetManifestResourceStream(name);
    }
}