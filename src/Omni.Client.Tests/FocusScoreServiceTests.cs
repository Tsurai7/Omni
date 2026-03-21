using System.Net;
using Omni.Client.Services;
using Omni.Client.Tests.Fakes;
using Omni.Client.Tests.Helpers;
using Xunit;

namespace Omni.Client.Tests;

public sealed class FocusScoreServiceTests
{
    private static (FocusScoreService Service, MockHttpMessageHandler Handler, FakeAuthService Auth) Build()
    {
        var (client, handler) = TestHttpClientFactory.Create();
        var auth = new FakeAuthService
        {
            Token = JwtHelper.ValidToken,
            User  = new Omni.Client.Models.Auth.UserResponse("user-1", "test@example.com")
        };
        var svc = new FocusScoreService(client, auth, TestHttpClientFactory.JsonOptions);
        return (svc, handler, auth);
    }

    [Fact]
    public async Task GetFocusScore_NoUser_ReturnsNull()
    {
        var (svc, _, auth) = Build();
        auth.User = null;

        var result = await svc.GetFocusScoreAsync();

        Assert.Null(result);
    }

    [Fact]
    public async Task GetFocusScore_Success_ReturnsScore()
    {
        var (svc, handler, _) = Build();
        handler.Respond(HttpStatusCode.OK, """
            {"score":82,"trend":"up","focus_minutes_today":95,"sessions_today":3,"streak_days":5,
             "breakdown":{"focus_ratio":85,"session_completion":90,"distraction_penalty":5,"consistency_bonus":7}}
            """);

        var result = await svc.GetFocusScoreAsync();

        Assert.NotNull(result);
        Assert.Equal(82, result!.Score);
        Assert.Equal("up", result.Trend);
        Assert.Equal(95, result.FocusMinutesToday);
        Assert.Equal(5, result.StreakDays);
        Assert.Equal(85, result.Breakdown.FocusRatio);
    }

    [Fact]
    public async Task GetFocusScore_ServerError_ReturnsNull()
    {
        var (svc, handler, _) = Build();
        handler.Respond(HttpStatusCode.InternalServerError);

        var result = await svc.GetFocusScoreAsync();

        Assert.Null(result);
    }

    [Fact]
    public async Task GetFocusScore_NetworkError_ReturnsNull()
    {
        var (svc, handler, _) = Build();
        handler.RespondWithNetworkError();

        var result = await svc.GetFocusScoreAsync();

        Assert.Null(result);
    }

    [Fact]
    public async Task GetFocusScore_RequestIncludesAuthHeader()
    {
        var (svc, handler, _) = Build();
        handler.Respond(HttpStatusCode.OK, """{"score":70,"trend":"flat","focus_minutes_today":0,"sessions_today":0,"streak_days":0,"breakdown":{"focus_ratio":0,"session_completion":0,"distraction_penalty":0,"consistency_bonus":0}}""");

        await svc.GetFocusScoreAsync();

        var auth = handler.Requests[0].Headers.Authorization;
        Assert.Equal("Bearer", auth?.Scheme);
        Assert.False(string.IsNullOrEmpty(auth?.Parameter));
    }

    [Fact]
    public async Task GetFocusScore_RequestUrlContainsUserId()
    {
        var (svc, handler, _) = Build();
        handler.Respond(HttpStatusCode.OK, """{"score":0,"trend":"flat","focus_minutes_today":0,"sessions_today":0,"streak_days":0,"breakdown":{"focus_ratio":0,"session_completion":0,"distraction_penalty":0,"consistency_bonus":0}}""");

        await svc.GetFocusScoreAsync();

        Assert.Contains("user-1", handler.Requests[0].RequestUri!.ToString());
    }
}
