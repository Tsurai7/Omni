using System.Diagnostics;
using Omni.Client.Abstractions;
using Omni.Client.Core.Abstractions.Api;
using Omni.Client.Models.FocusScore;
using Refit;

namespace Omni.Client.Services;

public class FocusScoreService : IFocusScoreService
{
    private readonly IAiApi _api;
    private readonly IAuthService _auth;

    public FocusScoreService(
        IAiApi api,
        IAuthService auth)
    {
        _api = api;
        _auth = auth;
    }

    public async Task<FocusScoreResponse?> GetFocusScoreAsync()
    {
        try
        {
            var user = await _auth.GetCurrentUserAsync();
            if (user == null) return null;

            return await _api.GetFocusScoreAsync(user.Id);
        }
        catch (ApiException ex)
        {
            Debug.WriteLine($"FocusScoreService.GetFocusScoreAsync: {ex.StatusCode}");
            return null;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"FocusScoreService.GetFocusScoreAsync: {ex.Message}");
            return null;
        }
    }
}
