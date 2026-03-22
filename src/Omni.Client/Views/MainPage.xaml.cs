using Omni.Client.Controls;
using Omni.Client.Presentation.ViewModels;

namespace Omni.Client;

public partial class MainPage : ContentPage
{
    private readonly MainViewModel _vm;
    private readonly FocusScoreRingDrawable _ringDrawable = new();
    private readonly System.Timers.Timer _uiTimer;
    private const int UiUpdateIntervalMs = 2500;

    public MainPage(MainViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        BindingContext = vm;

        ScoreRingView.Drawable = _ringDrawable;

        MainNetworkBanner.RetryAction = () =>
        {
            _ = Task.Run(async () => await vm.LoadAllRemoteDataAsync());
        };

        _vm.FocusScoreUpdated += (score, trend) =>
        {
            Dispatcher.Dispatch(() =>
            {
                _ringDrawable.Score = score;
                _ringDrawable.Trend = trend;
                ScoreRingView.Invalidate();
            });
        };

        _uiTimer = new System.Timers.Timer(UiUpdateIntervalMs);
        _uiTimer.Elapsed += (s, e) => Dispatcher.Dispatch(() => _vm.UpdateAppList());
        _uiTimer.AutoReset = true;

        _vm.StartTracking();
        _uiTimer.Start();
        _vm.UpdateAppList();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (!await _vm.IsAuthenticatedAsync())
        {
            await Shell.Current.GoToAsync(nameof(LoginPage));
            return;
        }
        _vm.StartUsageSync();
        _vm.LoadGoalFromPreferences();
        _ = _vm.LoadAllRemoteDataAsync().ContinueWith(async t =>
        {
            if (!t.IsCompletedSuccessfully)
                await MainNetworkBanner.ShowBannerAsync();
        });
        _uiTimer?.Start();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _uiTimer?.Stop();
    }

    private async void OnLogoutClicked(object? sender, EventArgs e)
    {
        _vm.StopUsageSync();
        _vm.Logout();
        await Shell.Current.GoToAsync(nameof(LoginPage));
    }

    private async void OnStartSessionClicked(object? sender, EventArgs e)
        => await Shell.Current.GoToAsync("///SessionPage");

    private async void OnAddTaskClicked(object? sender, EventArgs e)
        => await Shell.Current.GoToAsync("///TasksPage");
}
