using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Omni.Client.Abstractions;
using Omni.Client.Models.Chat;

namespace Omni.Client.Presentation.ViewModels;

public partial class ChatViewModel : ObservableObject
{
    private readonly IChatService _chatService;
    private readonly ITaskService _taskService;
    private CancellationTokenSource? _streamCts;

    [ObservableProperty]
    private ObservableCollection<ChatMessageViewModel> _messages = [];

    [ObservableProperty]
    private ObservableCollection<StarterViewModel> _starters = [];

    [ObservableProperty]
    private ObservableCollection<ConversationViewModel> _conversations = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSend))]
    private string _inputText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSend))]
    private bool _isStreaming;

    [ObservableProperty]
    private bool _showHistory;

    [ObservableProperty]
    private bool _showStarters;

    private string? _currentConversationId;

    public bool CanSend => !IsStreaming && !string.IsNullOrWhiteSpace(InputText);

    public event Action<ChatMessageViewModel>? MessageAdded;
    public event Action? StreamingFinished;

    public ChatViewModel(IChatService chatService, ITaskService taskService)
    {
        _chatService = chatService;
        _taskService = taskService;
    }

    [RelayCommand]
    public async Task LoadStartersAsync(CancellationToken ct = default)
    {
        try
        {
            var rawStarters = await _chatService.GetStartersAsync(ct);
            Starters = new ObservableCollection<StarterViewModel>(
                rawStarters.Select(s => new StarterViewModel { Text = s.Text, Icon = s.Icon ?? "insight" }));
            ShowStarters = Starters.Count > 0 && Messages.Count == 0;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"ChatViewModel.LoadStartersAsync: {ex.Message}");
        }
    }

    [RelayCommand]
    public async Task LoadConversationsAsync(CancellationToken ct = default)
    {
        try
        {
            var convs = await _chatService.GetConversationsAsync(ct);
            Conversations = new ObservableCollection<ConversationViewModel>(
                convs.Select(c => new ConversationViewModel
                {
                    Id = c.Id,
                    Title = c.Title ?? "(no title)",
                    LastMessageAtRaw = c.LastMessageAt
                }));
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"ChatViewModel.LoadConversationsAsync: {ex.Message}");
        }
    }

    [RelayCommand]
    public async Task OpenConversationAsync(ConversationViewModel conv, CancellationToken ct = default)
    {
        _currentConversationId = conv.Id;
        ShowHistory = false;

        try
        {
            var msgs = await _chatService.GetMessagesAsync(conv.Id, 20, ct);
            Messages = new ObservableCollection<ChatMessageViewModel>(
                msgs.Select(m => new ChatMessageViewModel
                {
                    Role = m.Role,
                    Content = m.Content,
                    Actions = m.Metadata?.Actions ?? []
                }));
            ShowStarters = false;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"ChatViewModel.OpenConversationAsync: {ex.Message}");
        }
    }

    [RelayCommand]
    public async Task DeleteConversationAsync(ConversationViewModel conv, CancellationToken ct = default)
    {
        try
        {
            await _chatService.DeleteConversationAsync(conv.Id, ct);
            Conversations.Remove(conv);
            if (_currentConversationId == conv.Id)
            {
                _currentConversationId = null;
                Messages.Clear();
                ShowStarters = Starters.Count > 0;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"ChatViewModel.DeleteConversationAsync: {ex.Message}");
        }
    }

    [RelayCommand(CanExecute = nameof(CanSend))]
    public async Task SendAsync(CancellationToken ct = default)
    {
        var text = InputText.Trim();
        if (string.IsNullOrEmpty(text)) return;

        InputText = string.Empty;
        ShowStarters = false;
        IsStreaming = true;

        var userMsg = new ChatMessageViewModel { Role = "user", Content = text };
        Messages.Add(userMsg);
        MessageAdded?.Invoke(userMsg);

        var assistantMsg = new ChatMessageViewModel { Role = "assistant", Content = "" };
        Messages.Add(assistantMsg);
        MessageAdded?.Invoke(assistantMsg);

        _streamCts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        try
        {
            await foreach (var delta in _chatService.SendMessageAsync(_currentConversationId, text, _streamCts.Token))
            {
                if (delta.ConversationId != null)
                    _currentConversationId = delta.ConversationId;

                if (delta.Error == true && delta.Delta != null)
                {
                    // Error delta from Gemini (quota, region, etc.) — show as error bubble
                    assistantMsg.Content = delta.Delta;
                    assistantMsg.IsError = true;
                    break;
                }

                if (delta.Delta != null)
                    assistantMsg.Content += delta.Delta;

                if (delta.Done == true)
                {
                    if (delta.Actions?.Count > 0)
                        assistantMsg.Actions = delta.Actions;
                    break;
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Debug.WriteLine($"ChatViewModel.SendAsync: {ex.Message}");
            assistantMsg.Content = "Something went wrong. Please try again.";
            assistantMsg.IsError = true;
        }
        finally
        {
            IsStreaming = false;
            StreamingFinished?.Invoke();
        }
    }

    public async Task StarterTappedAsync(StarterViewModel starter)
    {
        InputText = starter.Text;
        await SendAsync();
    }

    public async Task HandleActionAsync(ChatAction action)
    {
        var taskTitle = action.Title ?? action.Label;
        if (action.Type == "create_task" && !string.IsNullOrEmpty(taskTitle))
        {
            try
            {
                await _taskService.CreateTaskAsync(taskTitle);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ChatViewModel.HandleActionAsync create_task: {ex.Message}");
            }
        }
    }

    public void CancelStream()
    {
        _streamCts?.Cancel();
        _streamCts = null;
    }

    public void NewConversation()
    {
        _currentConversationId = null;
        Messages.Clear();
        ShowStarters = Starters.Count > 0;
    }

    public void ToggleHistory() => ShowHistory = !ShowHistory;
}
