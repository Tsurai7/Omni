using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using Omni.Client.Abstractions;
using Omni.Client.Models.FocusScore;

namespace Omni.Client.Services;

public class FocusScoreService : IFocusScoreService
{
    private readonly HttpClient _http;
    private readonly IAuthService _auth;
    private readonly JsonSerializerOptions _json;

    public FocusScoreService(HttpClient http, IAuthService auth, JsonSerializerOptions json)
    {
        _http = http;
        _auth = auth;
        _json = json;
    }

    public async Task<FocusScoreResponse?> GetFocusScoreAsync()
    {
        try
        {
            var user = await _auth.GetCurrentUserAsync();
            if (user == null) return null;

            var token = await _auth.GetTokenAsync();
            using var request = new HttpRequestMessage(HttpMethod.Get, $"api/ai/focus-score/{user.Id}");
            if (!string.IsNullOrEmpty(token))
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

            using var response = await _http.SendAsync(request);
            if (!response.IsSuccessStatusCode) return null;

            return await response.Content.ReadFromJsonAsync<FocusScoreResponse>(_json);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"FocusScoreService.GetFocusScoreAsync: {ex.Message}");
            return null;
        }
    }
}
