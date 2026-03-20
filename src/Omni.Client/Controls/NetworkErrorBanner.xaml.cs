using Omni.Client.Services;

namespace Omni.Client.Controls;

public partial class NetworkErrorBanner : ContentView
{
    /// <summary>Action to invoke when the user taps Retry.</summary>
    public Action? RetryAction { get; set; }

    public NetworkErrorBanner()
    {
        InitializeComponent();

        // React to global online-state changes
        NetworkStatusService.Instance.OnlineStatusChanged += OnOnlineStatusChanged;
    }

    private void OnOnlineStatusChanged(object? sender, bool isOnline)
    {
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            if (isOnline)
                await HideBannerAsync();
            else
                await ShowBannerAsync();
        });
    }

    public async Task ShowBannerAsync(string? title = null, string? subtitle = null)
    {
        if (title != null) TitleLabel.Text = title;
        if (subtitle != null) SubtitleLabel.Text = subtitle;

        if (IsVisible) return;
        IsVisible = true;
        Opacity = 0;
        await this.FadeToAsync(1, 220, Easing.CubicOut);
    }

    public async Task HideBannerAsync()
    {
        if (!IsVisible) return;
        await this.FadeToAsync(0, 180, Easing.CubicIn);
        IsVisible = false;
    }

    private async void OnRetryClicked(object? sender, EventArgs e)
    {
        RetryButton.IsEnabled = false;
        RetryButton.Text = "…";
        try
        {
            RetryAction?.Invoke();
            // Give the load a moment, then re-check IsOnline
            await Task.Delay(1200);
        }
        finally
        {
            RetryButton.IsEnabled = true;
            RetryButton.Text = "Retry";
        }
    }

    protected override void OnHandlerChanging(HandlerChangingEventArgs args)
    {
        base.OnHandlerChanging(args);
        if (args.NewHandler == null)
            NetworkStatusService.Instance.OnlineStatusChanged -= OnOnlineStatusChanged;
    }
}
