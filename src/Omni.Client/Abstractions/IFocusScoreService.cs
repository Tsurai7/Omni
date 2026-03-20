using Omni.Client.Models.FocusScore;

namespace Omni.Client.Abstractions;

public interface IFocusScoreService
{
    /// <summary>Fetch today's focus score for the current user. Returns null on error or not authenticated.</summary>
    Task<FocusScoreResponse?> GetFocusScoreAsync();
}
