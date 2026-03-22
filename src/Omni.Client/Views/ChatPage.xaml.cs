using System.ComponentModel;
using System.Runtime.CompilerServices;
using Omni.Client.Abstractions;
using Omni.Client.Models.Chat;
using Omni.Client.Presentation.ViewModels;

namespace Omni.Client;

public class ChatMessageViewModel : INotifyPropertyChanged
{
    private string _content = string.Empty;
    private List<ChatAction> _actions = [];

    public string Id { get; init; } = Guid.NewGuid().ToString();
    public string Role { get; init; } = "user";

    public List<ChatAction> Actions
    {
        get => _actions;
        set
        {
            _actions = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasActions));
        }
    }

    public string Content
    {
        get => _content;
        set { if (_content != value) { _content = value; OnPropertyChanged(); } }
    }

    public bool IsUser => Role == "user";
    public bool IsAssistant => Role == "assistant";
    public bool HasActions => _actions.Count > 0;

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? n = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}

public class ConversationViewModel
{
    public string Id { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string? LastMessageAtRaw { get; init; }

    public string LastMessageAtDisplay => DateTimeOffset.TryParse(LastMessageAtRaw, out var dt)
        ? dt.LocalDateTime.ToString("MMM d, h:mm tt")
        : string.Empty;
}

public class StarterViewModel
{
    public string Text { get; init; } = string.Empty;
    public string Icon { get; init; } = "insight";

    public string IconEmoji => Icon switch
    {
        "streak" => "🔥",
        "focus"  => "⚡",
        "task"   => "📋",
        _        => "✦",
    };
}

public partial class ChatPage : ContentPage
{
    private readonly Presentation.ViewModels.ChatViewModel _vm;
    private bool _dotAnimRunning;

    public ChatPage(Presentation.ViewModels.ChatViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        BindingContext = vm;

        vm.MessageAdded += async msg => await ScrollToBottomAsync();
        vm.StreamingFinished += async () =>
        {
            await ScrollToBottomAsync();
        };

        vm.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(vm.IsStreaming) && vm.IsStreaming)
                StartDotAnimation();
        };
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        var auth = MauiProgram.AppServices?.GetService<IAuthService>();
        if (auth != null && !await auth.IsAuthenticatedAsync())
        {
            await Shell.Current.GoToAsync(nameof(LoginPage));
            return;
        }
        if (_vm.Messages.Count == 0)
            await _vm.LoadStartersAsync();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _vm.CancelStream();
    }

    private void OnMessageEditorTextChanged(object? sender, TextChangedEventArgs e)
    {
        var live = e.NewTextValue ?? string.Empty;
        if (_vm.InputText != live)
            _vm.InputText = live;
    }

    private async void OnMessageEditorCompleted(object? sender, EventArgs e)
    {
        if (!_vm.IsStreaming && !string.IsNullOrWhiteSpace(MessageEditor?.Text ?? _vm.InputText))
            await _vm.SendAsync();
    }

    private async void OnHistoryClicked(object? sender, EventArgs e)
    {
        await _vm.LoadConversationsAsync();
        _vm.ToggleHistory();
    }

    private void OnHistoryCloseClicked(object? sender, EventArgs e)
        => _vm.ShowHistory = false;

    private async void OnNewConversationClicked(object? sender, EventArgs e)
        => _vm.NewConversation();

    private async void OnStarterTapped(object? sender, TappedEventArgs e)
    {
        if (sender is BindableObject bo && bo.BindingContext is StarterViewModel starter)
            await _vm.StarterTappedAsync(starter);
    }

    private async void OnActionTapped(object? sender, EventArgs e)
    {
        if (sender is BindableObject bo && bo.BindingContext is ChatAction action)
            await HandleActionAsync(action);
    }

    private async void OnConversationTapped(object? sender, TappedEventArgs e)
    {
        if (sender is BindableObject bo && bo.BindingContext is ConversationViewModel conv)
            await _vm.OpenConversationAsync(conv);
    }

    private async Task HandleActionAsync(ChatAction action)
    {
        switch (action.Type)
        {
            case "start_session":
                MauiProgram.AppServices?.GetService<SessionPage>()?.SetNavigatedFromChat();
                await Shell.Current.GoToAsync("///SessionPage");
                break;
            case "create_task":
                if (!string.IsNullOrEmpty(action.Title))
                {
                    await _vm.HandleActionAsync(action);
                    await DisplayAlertAsync("Task created", $"\"{action.Title}\" added to your tasks.", "OK");
                }
                break;
            case "take_break":
                await DisplayAlertAsync("Take a break", "Step away for 5 minutes. Come back refreshed.", "Got it");
                break;
            case "view_stats":
                await Shell.Current.GoToAsync("///UsageStatsPage");
                break;
        }
    }

    private async Task ScrollToBottomAsync()
    {
        try
        {
            await Task.Delay(50);
            if (_vm.Messages.Count > 0)
                MessageList.ScrollTo(_vm.Messages[^1], position: ScrollToPosition.End, animate: false);
        }
        catch { }
    }

    private void StartDotAnimation()
    {
        if (_dotAnimRunning) return;
        _dotAnimRunning = true;
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            while (_vm.IsStreaming)
            {
                await Dot1.FadeToAsync(1, 200);
                await Dot2.FadeToAsync(1, 200);
                await Dot3.FadeToAsync(1, 200);
                await Dot1.FadeToAsync(0.2, 200);
                await Dot2.FadeToAsync(0.2, 200);
                await Dot3.FadeToAsync(0.2, 200);
            }
            Dot1.Opacity = 0.4;
            Dot2.Opacity = 0.4;
            Dot3.Opacity = 0.4;
            _dotAnimRunning = false;
        });
    }
}
