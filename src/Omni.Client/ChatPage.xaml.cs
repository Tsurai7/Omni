using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Omni.Client.Abstractions;
using Omni.Client.Models.Chat;

namespace Omni.Client;

// ── View model for a single chat message ─────────────────────────────────────

public class ChatMessageViewModel : INotifyPropertyChanged
{
    private string _content = string.Empty;

    public string Id { get; init; } = Guid.NewGuid().ToString();
    public string Role { get; init; } = "user";
    public List<ChatAction> Actions { get; init; } = [];

    public string Content
    {
        get => _content;
        set { if (_content != value) { _content = value; OnPropertyChanged(); } }
    }

    public bool IsUser => Role == "user";
    public bool IsAssistant => Role == "assistant";
    public bool HasActions => Actions.Count > 0;

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? n = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}

// ── Starter view model ────────────────────────────────────────────────────────

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

// ── ChatPage ──────────────────────────────────────────────────────────────────

public partial class ChatPage : ContentPage, INotifyPropertyChanged
{
    private IChatService? _chatService;
    private ITaskService? _taskService;
    private IAuthService? _authService;

    private ObservableCollection<ChatMessageViewModel> _messages = [];
    private ObservableCollection<StarterViewModel> _starters = [];
    private string _inputText = string.Empty;
    private bool _isStreaming;
    private string? _currentConversationId;
    private CancellationTokenSource? _streamCts;

    // Typing indicator animation
    private bool _dotAnimRunning;

    public ChatPage()
    {
        InitializeComponent();
        BindingContext = this;

        SendCommand = new Command(
            async () => await SendMessageAsync(),
            () => CanSend);

        StarterTappedCommand = new Command<string>(async text =>
        {
            if (!string.IsNullOrEmpty(text))
            {
                InputText = text;
                await SendMessageAsync();
            }
        });

        ActionTappedCommand = new Command<ChatAction?>(async action =>
        {
            if (action == null) return;
            await HandleActionAsync(action);
        });
    }

    // ── Commands ──────────────────────────────────────────────────────────────

    public ICommand SendCommand { get; }
    public ICommand StarterTappedCommand { get; }
    public ICommand ActionTappedCommand { get; }

    // ── Bindable properties ───────────────────────────────────────────────────

    public ObservableCollection<ChatMessageViewModel> Messages
    {
        get => _messages;
        set { _messages = value; OnPropertyChanged(); OnPropertyChanged(nameof(ShowWelcome)); }
    }

    public ObservableCollection<StarterViewModel> Starters
    {
        get => _starters;
        set { _starters = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasStarters)); }
    }

    public string InputText
    {
        get => _inputText;
        set
        {
            if (_inputText != value)
            {
                _inputText = value;
                OnPropertyChanged();
                ((Command)SendCommand).ChangeCanExecute();
            }
        }
    }

    public bool IsStreaming
    {
        get => _isStreaming;
        set
        {
            if (_isStreaming != value)
            {
                _isStreaming = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CanSend));
                OnPropertyChanged(nameof(IsNotStreaming));
                ((Command)SendCommand).ChangeCanExecute();
                if (value) StartDotAnimation();
            }
        }
    }

    public bool CanSend => !IsStreaming && !string.IsNullOrWhiteSpace(InputText);
    public bool ShowWelcome => Messages.Count == 0;
    public bool HasStarters => Starters.Count > 0;
    // Bound to Editor.IsEnabled — user can type but not send while streaming
    public bool IsNotStreaming => !IsStreaming;

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        var auth = GetAuthService();
        if (auth != null && !await auth.IsAuthenticatedAsync())
        {
            await Shell.Current.GoToAsync(nameof(LoginPage));
            return;
        }

        // Load starters only when starting fresh
        if (Messages.Count == 0)
            await LoadStartersAsync();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _streamCts?.Cancel();
    }

    // ── Service helpers ───────────────────────────────────────────────────────

    private IChatService GetChatService() =>
        _chatService ??= MauiProgram.AppServices!.GetRequiredService<IChatService>();

    private ITaskService GetTaskService() =>
        _taskService ??= MauiProgram.AppServices!.GetRequiredService<ITaskService>();

    private IAuthService? GetAuthService() =>
        _authService ??= MauiProgram.AppServices?.GetService<IAuthService>();

    // ── Load starters ─────────────────────────────────────────────────────────

    private async Task LoadStartersAsync()
    {
        try
        {
            var starters = await GetChatService().GetStartersAsync();
            Starters = new ObservableCollection<StarterViewModel>(
                starters.Select(s => new StarterViewModel { Text = s.Text, Icon = s.Icon }));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"ChatPage.LoadStartersAsync: {ex.Message}");
        }
    }

    // ── Send message ──────────────────────────────────────────────────────────

    private async Task SendMessageAsync()
    {
        var text = InputText.Trim();
        if (string.IsNullOrEmpty(text) || IsStreaming) return;

        InputText = string.Empty;
        IsStreaming = true;

        // Hide welcome panel by adding user message
        var userVm = new ChatMessageViewModel { Role = "user", Content = text };
        Messages.Add(userVm);
        OnPropertyChanged(nameof(ShowWelcome));
        await ScrollToBottomAsync();

        // Placeholder for streaming assistant response
        var assistantVm = new ChatMessageViewModel { Role = "assistant", Content = "" };
        Messages.Add(assistantVm);
        await ScrollToBottomAsync();

        _streamCts = new CancellationTokenSource();
        string? resolvedConvId = _currentConversationId;

        try
        {
            await foreach (var delta in GetChatService()
                .SendMessageAsync(_currentConversationId, text, _streamCts.Token))
            {
                if (delta.ConversationId != null)
                    resolvedConvId = delta.ConversationId;

                if (delta.Done == true) break;

                if (!string.IsNullOrEmpty(delta.Delta))
                {
                    assistantVm.Content += delta.Delta;
                    await ScrollToBottomAsync();
                }
            }
        }
        catch (OperationCanceledException) { /* user navigated away */ }
        catch (Exception ex)
        {
            assistantVm.Content = "Sorry, something went wrong. Try again.";
            System.Diagnostics.Debug.WriteLine($"ChatPage stream error: {ex.Message}");
        }
        finally
        {
            _currentConversationId = resolvedConvId;
            IsStreaming = false;
            _streamCts?.Dispose();
            _streamCts = null;
        }

        // Reload message from API to get metadata/actions
        if (_currentConversationId != null)
            await RefreshLastAssistantMessageAsync(assistantVm);
    }

    // ── Refresh last message to pick up metadata (actions etc) ───────────────

    private async Task RefreshLastAssistantMessageAsync(ChatMessageViewModel vm)
    {
        try
        {
            if (_currentConversationId == null) return;
            var msgs = await GetChatService().GetMessagesAsync(_currentConversationId, limit: 2);
            var last = msgs.LastOrDefault(m => m.Role == "assistant");
            if (last?.Metadata?.Actions is { Count: > 0 } actions)
            {
                // Rebuild with actions — replace the vm in collection
                var idx = Messages.IndexOf(vm);
                if (idx >= 0)
                {
                    Messages[idx] = new ChatMessageViewModel
                    {
                        Role = "assistant",
                        Content = vm.Content,
                        Actions = actions,
                    };
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"ChatPage.RefreshLastAssistantMessage: {ex.Message}");
        }
    }

    // ── Action handler ────────────────────────────────────────────────────────

    private async Task HandleActionAsync(ChatAction action)
    {
        switch (action.Type)
        {
            case "start_session":
                await Shell.Current.GoToAsync(nameof(SessionPage));
                break;

            case "create_task":
                if (!string.IsNullOrEmpty(action.Title))
                {
                    await GetTaskService().CreateTaskAsync(action.Title);
                    await DisplayAlertAsync("Task created", $"\"{action.Title}\" added to your tasks.", "OK");
                }
                break;

            case "take_break":
                await DisplayAlertAsync("Take a break", "Step away for 5 minutes. Come back refreshed.", "Got it");
                break;

            case "view_stats":
                await Shell.Current.GoToAsync(nameof(UsageStatsPage));
                break;
        }
    }

    // ── History ───────────────────────────────────────────────────────────────

    private async void OnHistoryClicked(object? sender, EventArgs e)
    {
        try
        {
            var conversations = await GetChatService().GetConversationsAsync();
            if (conversations.Count == 0)
            {
                await DisplayAlertAsync("No history", "You haven't had any conversations yet.", "OK");
                return;
            }

            var options = conversations.Select(c => c.Title).ToArray();
            var choice = await DisplayActionSheetAsync("Previous conversations", "Cancel", "New conversation", options);

            if (choice == "Cancel") return;

            if (choice == "New conversation")
            {
                Messages.Clear();
                _currentConversationId = null;
                OnPropertyChanged(nameof(ShowWelcome));
                await LoadStartersAsync();
                return;
            }

            var selected = conversations.FirstOrDefault(c => c.Title == choice);
            if (selected == null) return;

            var msgs = await GetChatService().GetMessagesAsync(selected.Id, limit: 20);
            _currentConversationId = selected.Id;
            Messages.Clear();
            foreach (var m in msgs)
            {
                Messages.Add(new ChatMessageViewModel
                {
                    Role = m.Role,
                    Content = m.Content,
                    Actions = m.Metadata?.Actions ?? [],
                });
            }
            OnPropertyChanged(nameof(ShowWelcome));
            await ScrollToBottomAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"ChatPage.OnHistoryClicked: {ex.Message}");
        }
    }

    // ── Scroll helper ─────────────────────────────────────────────────────────

    private async Task ScrollToBottomAsync()
    {
        try
        {
            await Task.Delay(50);
            if (Messages.Count > 0)
                MessageList.ScrollTo(Messages[^1], position: ScrollToPosition.End, animate: false);
        }
        catch { /* ignore scroll errors */ }
    }

    // ── Typing indicator animation ────────────────────────────────────────────

    private void StartDotAnimation()
    {
        if (_dotAnimRunning) return;
        _dotAnimRunning = true;
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            while (IsStreaming)
            {
                await Dot1.FadeTo(1, 200);
                await Dot2.FadeTo(1, 200);
                await Dot3.FadeTo(1, 200);
                await Dot1.FadeTo(0.2, 200);
                await Dot2.FadeTo(0.2, 200);
                await Dot3.FadeTo(0.2, 200);
            }
            Dot1.Opacity = 0.4;
            Dot2.Opacity = 0.4;
            Dot3.Opacity = 0.4;
            _dotAnimRunning = false;
        });
    }

    // ── DisplayAlert wrappers (avoids ambiguous call warnings) ───────────────

    private Task<bool> DisplayAlertAsync(string title, string message, string accept, string cancel) =>
        base.DisplayAlert(title, message, accept, cancel);

    private Task DisplayAlertAsync(string title, string message, string cancel) =>
        base.DisplayAlert(title, message, cancel);

    private Task<string> DisplayActionSheetAsync(string title, string cancel, string? destruction, params string[] buttons) =>
        base.DisplayActionSheet(title, cancel, destruction, buttons);

    // ── INotifyPropertyChanged ────────────────────────────────────────────────

    public new event PropertyChangedEventHandler? PropertyChanged;
    protected new void OnPropertyChanged([CallerMemberName] string? n = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}
