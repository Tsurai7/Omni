namespace Omni.Client;

public sealed class BackendOptions
{
    public const string SectionName = "Backend";
    public string BaseUrl { get; set; } = "http://localhost:8080";
}
