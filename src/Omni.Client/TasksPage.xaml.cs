using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Omni.Client.Abstractions;
using Omni.Client.Models.Task;
using Omni.Client.Services;

namespace Omni.Client;

public partial class TasksPage : ContentPage, INotifyPropertyChanged
{
    private ITaskService? _taskService;
    private LocalDatabaseService? _localDb;
    private TaskDisplayItem? _draggingTask;

    private ObservableCollection<TaskDisplayItem> _todoTasks       = new();
    private ObservableCollection<TaskDisplayItem> _inProgressTasks = new();
    private ObservableCollection<TaskDisplayItem> _doneTasks       = new();

    public TasksPage()
    {
        InitializeComponent();
        BindingContext = this;

        EditTaskCommand = new Command<TaskDisplayItem?>(async item =>
        {
            if (item == null) return;
            await EditTaskAsync(item);
        });

        DeleteTaskCommand = new Command<TaskDisplayItem?>(async item =>
        {
            if (item == null) return;
            var confirm = await DisplayAlertAsync("Delete task", $"Remove \"{item.Title}\"?", "Delete", "Cancel");
            if (!confirm) return;
            await GetTaskService().DeleteTaskAsync(item.Id);
            await LoadAsync();
        });

        CyclePriorityCommand = new Command<TaskDisplayItem?>(async item =>
        {
            if (item == null) return;
            var nextPriority = item.Priority?.ToLowerInvariant() switch
            {
                "low"    => "medium",
                "medium" => "high",
                _        => "low",
            };
            await GetTaskService().UpdateTaskAsync(item.Id, item.Title, nextPriority);
            await LoadAsync();
        });
    }

    private ITaskService GetTaskService()
    {
        _taskService ??= MauiProgram.AppServices?.GetService<ITaskService>();
        return _taskService!;
    }

    private LocalDatabaseService GetLocalDb()
    {
        _localDb ??= MauiProgram.AppServices?.GetService<LocalDatabaseService>();
        return _localDb!;
    }

    public ICommand EditTaskCommand      { get; }
    public ICommand DeleteTaskCommand    { get; }
    public ICommand CyclePriorityCommand { get; }

    public ObservableCollection<TaskDisplayItem> TodoTasks
    {
        get => _todoTasks;
        set { _todoTasks = value; OnPropertyChanged(); OnPropertyChanged(nameof(TodoCount)); }
    }

    public ObservableCollection<TaskDisplayItem> InProgressTasks
    {
        get => _inProgressTasks;
        set { _inProgressTasks = value; OnPropertyChanged(); OnPropertyChanged(nameof(InProgressCount)); }
    }

    public ObservableCollection<TaskDisplayItem> DoneTasks
    {
        get => _doneTasks;
        set { _doneTasks = value; OnPropertyChanged(); OnPropertyChanged(nameof(DoneCount)); }
    }

    public int TodoCount       => _todoTasks.Count;
    public int InProgressCount => _inProgressTasks.Count;
    public int DoneCount       => _doneTasks.Count;

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        var auth = MauiProgram.AppServices?.GetService<IAuthService>();
        if (auth != null && !await auth.IsAuthenticatedAsync())
        {
            await Shell.Current.GoToAsync(nameof(LoginPage));
            return;
        }
        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        try
        {
            var taskService = GetTaskService();
            var localDb     = GetLocalDb();

            var fromApi   = await taskService.GetTasksAsync();
            var fromLocal = await localDb.GetAllTasksAsync();

            var merged = new List<TaskDisplayItem>();
            foreach (var t in fromApi)
                merged.Add(TaskDisplayItem.FromListItem(t));

            var serverIds = new HashSet<string>(merged.Select(x => x.Id));
            foreach (var t in fromLocal)
            {
                if (t.IsSynced && !string.IsNullOrEmpty(t.ServerId) && serverIds.Contains(t.ServerId!))
                    continue;
                if (!t.IsSynced || string.IsNullOrEmpty(t.ServerId))
                    merged.Add(TaskDisplayItem.FromLocalTask(t));
            }

            // Sort by priority (high first) then newest first
            var priorityOrder = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
                { ["high"] = 0, ["medium"] = 1, ["low"] = 2 };
            merged = merged
                .OrderBy(x => priorityOrder.GetValueOrDefault(x.Priority ?? "medium", 1))
                .ThenByDescending(x =>
                {
                    if (DateTime.TryParse(x.CreatedAt, out var dt)) return dt;
                    return DateTime.MinValue;
                })
                .ToList();

            TodoTasks       = new ObservableCollection<TaskDisplayItem>(merged.Where(t => t.IsPending));
            InProgressTasks = new ObservableCollection<TaskDisplayItem>(merged.Where(t => t.IsInProgress));
            DoneTasks       = new ObservableCollection<TaskDisplayItem>(merged.Where(t => t.IsDone));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"TasksPage.LoadAsync error: {ex}");
        }
    }

    // ── Add ──────────────────────────────────────────────────────────────
    private async void OnAddTaskClicked(object? sender, EventArgs e)
    {
        var title = await DisplayPromptAsync(
            "Add task",
            "What do you want to do?",
            "Add", "Cancel",
            placeholder: "e.g. Finish report");
        if (string.IsNullOrWhiteSpace(title)) return;

        var priority = await DisplayActionSheetAsync(
            "Priority", "Cancel", null,
            "High", "Medium", "Low");
        var p = priority?.ToLowerInvariant() switch
        {
            "high" => "high",
            "low"  => "low",
            _      => "medium",
        };

        await GetTaskService().CreateTaskAsync(title.Trim(), p);
        await LoadAsync();
    }

    // ── Edit ─────────────────────────────────────────────────────────────
    private async Task EditTaskAsync(TaskDisplayItem item)
    {
        var newTitle = await DisplayPromptAsync(
            "Edit task", "Update the task title:", "Save", "Cancel",
            initialValue: item.Title, placeholder: item.Title);
        if (newTitle == null) return; // user cancelled

        var priorityChoice = await DisplayActionSheetAsync(
            "Priority", "Keep current", null,
            "High", "Medium", "Low");
        var newPriority = priorityChoice?.ToLowerInvariant() switch
        {
            "high"   => "high",
            "medium" => "medium",
            "low"    => "low",
            _        => item.Priority, // "Keep current" or cancelled
        };

        var finalTitle = string.IsNullOrWhiteSpace(newTitle) ? item.Title : newTitle.Trim();
        await GetTaskService().UpdateTaskAsync(item.Id, finalTitle, newPriority ?? "medium");
        await LoadAsync();
    }

    // ── Drag & Drop ───────────────────────────────────────────────────────
    private void OnDragStarting(object? sender, DragStartingEventArgs e)
    {
        if (sender is View view && view.BindingContext is TaskDisplayItem task)
        {
            _draggingTask = task;
            e.Data.Properties["taskId"] = task.Id;
        }
    }

    private async void OnDropPending(object? sender, DropEventArgs e)
    {
        if (_draggingTask != null && !_draggingTask.IsPending)
        {
            await GetTaskService().UpdateStatusAsync(_draggingTask.Id, "pending");
            await LoadAsync();
        }
        _draggingTask = null;
    }

    private async void OnDropInProgress(object? sender, DropEventArgs e)
    {
        if (_draggingTask != null && !_draggingTask.IsInProgress)
        {
            await GetTaskService().UpdateStatusAsync(_draggingTask.Id, "in_progress");
            await LoadAsync();
        }
        _draggingTask = null;
    }

    private async void OnDropDone(object? sender, DropEventArgs e)
    {
        if (_draggingTask != null && !_draggingTask.IsDone)
        {
            await GetTaskService().UpdateStatusAsync(_draggingTask.Id, "done");
            await LoadAsync();
        }
        _draggingTask = null;
    }

    // ── INotifyPropertyChanged ────────────────────────────────────────────
    public new event PropertyChangedEventHandler? PropertyChanged;
    protected new void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
