namespace Omni.Client.Abstractions;

/// <summary>Background service that drains unsynced local data to the API.</summary>
public interface ISyncService
{
    void StartPeriodicSync();
    void StopPeriodicSync();
}
