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

    private ObservableCollection<TaskDisplayItem> _todoTasks       = new();
    private ObservableCollection<TaskDisplayItem> _inProgressTasks = new();
    private ObservableCollection<TaskDisplayItem> _doneTasks       = new();

    private string _selectedFilter = "All";
    private bool   _isDoneExpanded = false;

    private static readonly Color PillActiveBg   = Color.FromArgb("#4ECCA3");
    private static readonly Color PillActiveFg   = Color.FromArgb("#0F1210");
    private static readonly Color PillInactiveBg = Color.FromArgb("#222228");
    private static readonly Color PillInactiveFg = Color.FromArgb("#9898A8");

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

    // ── Collections ──────────────────────────────────────────────────────

    public ObservableCollection<TaskDisplayItem> TodoTasks
    {
        get => _todoTasks;
        set { _todoTasks = value; OnPropertyChanged(); NotifyCountDependents(); }
    }

    public ObservableCollection<TaskDisplayItem> InProgressTasks
    {
        get => _inProgressTasks;
        set { _inProgressTasks = value; OnPropertyChanged(); NotifyCountDependents(); }
    }

    public ObservableCollection<TaskDisplayItem> DoneTasks
    {
        get => _doneTasks;
        set { _doneTasks = value; OnPropertyChanged(); NotifyCountDependents(); }
    }

    public int TodoCount       => _todoTasks.Count;
    public int InProgressCount => _inProgressTasks.Count;
    public int DoneCount       => _doneTasks.Count;
    public int TotalCount      => TodoCount + InProgressCount + DoneCount;

    // ── Progress / Empty ─────────────────────────────────────────────────

    public string ProgressSummary => TotalCount == 0
        ? "No tasks yet"
        : $"{DoneCount} of {TotalCount} done";

    public bool HasNoTasks => TotalCount == 0;

    // ── Filter ───────────────────────────────────────────────────────────

    public string SelectedFilter
    {
        get => _selectedFilter;
        set
        {
            _selectedFilter = value;
            if (value == "Done") _isDoneExpanded = true;
            NotifyFilterDependents();
        }
    }

    public bool ShowTodoSection       => (SelectedFilter == "All" && TodoCount > 0) || SelectedFilter == "Todo";
    public bool ShowInProgressSection => (SelectedFilter == "All" && InProgressCount > 0) || SelectedFilter == "Active";
    public bool ShowDoneSection       => (SelectedFilter == "All" && DoneCount > 0) || SelectedFilter == "Done";
    public bool ShowDoneToggle        => ShowDoneSection && SelectedFilter == "All";
    public bool ShowDoneItems         => SelectedFilter == "Done" || (SelectedFilter == "All" && IsDoneExpanded);

    // Filter pill labels
    public string FilterTodoLabel   => TodoCount > 0       ? $"Todo ({TodoCount})"         : "Todo";
    public string FilterActiveLabel => InProgressCount > 0 ? $"Active ({InProgressCount})" : "Active";
    public string FilterDoneLabel   => DoneCount > 0       ? $"Done ({DoneCount})"         : "Done";

    // Filter pill colors
    public Color FilterAllBg    => SelectedFilter == "All"    ? PillActiveBg : PillInactiveBg;
    public Color FilterAllFg    => SelectedFilter == "All"    ? PillActiveFg : PillInactiveFg;
    public Color FilterTodoBg   => SelectedFilter == "Todo"   ? PillActiveBg : PillInactiveBg;
    public Color FilterTodoFg   => SelectedFilter == "Todo"   ? PillActiveFg : PillInactiveFg;
    public Color FilterActiveBg => SelectedFilter == "Active" ? PillActiveBg : PillInactiveBg;
    public Color FilterActiveFg => SelectedFilter == "Active" ? PillActiveFg : PillInactiveFg;
    public Color FilterDoneBg   => SelectedFilter == "Done"   ? PillActiveBg : PillInactiveBg;
    public Color FilterDoneFg   => SelectedFilter == "Done"   ? PillActiveFg : PillInactiveFg;

    // ── Done collapse ─────────────────────────────────────────────────────

    public bool IsDoneExpanded
    {
        get => _isDoneExpanded;
        set
        {
            _isDoneExpanded = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(DoneToggleLabel));
            OnPropertyChanged(nameof(ShowDoneItems));
        }
    }

    public string DoneToggleLabel => IsDoneExpanded
        ? $"▴  Hide completed ({DoneCount})"
        : $"▾  Show completed ({DoneCount})";

    // ── Filter handlers ───────────────────────────────────────────────────

    private void OnFilterAll(object? sender, EventArgs e)    => SelectedFilter = "All";
    private void OnFilterTodo(object? sender, EventArgs e)   => SelectedFilter = "Todo";
    private void OnFilterActive(object? sender, EventArgs e) => SelectedFilter = "Active";
    private void OnFilterDone(object? sender, EventArgs e)   => SelectedFilter = "Done";

    private void OnToggleDoneExpanded(object? sender, EventArgs e) => IsDoneExpanded = !IsDoneExpanded;

    // ── Change notification helpers ───────────────────────────────────────

    private void NotifyCountDependents()
    {
        OnPropertyChanged(nameof(TodoCount));
        OnPropertyChanged(nameof(InProgressCount));
        OnPropertyChanged(nameof(DoneCount));
        OnPropertyChanged(nameof(TotalCount));
        NotifyFilterDependents();
    }

    private void NotifyFilterDependents()
    {
        OnPropertyChanged(nameof(SelectedFilter));
        OnPropertyChanged(nameof(ProgressSummary));
        OnPropertyChanged(nameof(HasNoTasks));
        OnPropertyChanged(nameof(ShowTodoSection));
        OnPropertyChanged(nameof(ShowInProgressSection));
        OnPropertyChanged(nameof(ShowDoneSection));
        OnPropertyChanged(nameof(ShowDoneToggle));
        OnPropertyChanged(nameof(ShowDoneItems));
        OnPropertyChanged(nameof(FilterTodoLabel));
        OnPropertyChanged(nameof(FilterActiveLabel));
        OnPropertyChanged(nameof(FilterDoneLabel));
        OnPropertyChanged(nameof(DoneToggleLabel));
        OnPropertyChanged(nameof(FilterAllBg));
        OnPropertyChanged(nameof(FilterAllFg));
        OnPropertyChanged(nameof(FilterTodoBg));
        OnPropertyChanged(nameof(FilterTodoFg));
        OnPropertyChanged(nameof(FilterActiveBg));
        OnPropertyChanged(nameof(FilterActiveFg));
        OnPropertyChanged(nameof(FilterDoneBg));
        OnPropertyChanged(nameof(FilterDoneFg));
    }

    // ── Lifecycle ─────────────────────────────────────────────────────────

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

        var dueDateChoice = await DisplayActionSheetAsync(
            "Due date", "No due date", null,
            "Today", "Tomorrow", "This week", "Pick date");
        DateTime? dueDate = dueDateChoice switch
        {
            "Today"     => DateTime.Today,
            "Tomorrow"  => DateTime.Today.AddDays(1),
            "This week" => DateTime.Today.AddDays(7 - (int)DateTime.Today.DayOfWeek),
            "Pick date" => await PickDateAsync(),
            _           => null,
        };

        await GetTaskService().CreateTaskAsync(title.Trim(), p, dueDate);
        await LoadAsync();
    }

    private static Task<DateTime?> PickDateAsync()
    {
        return Task.FromResult<DateTime?>(DateTime.Today.AddDays(7));
    }

    // ── Edit ─────────────────────────────────────────────────────────────

    private async Task EditTaskAsync(TaskDisplayItem item)
    {
        var newTitle = await DisplayPromptAsync(
            "Edit task", "Update the task title:", "Save", "Cancel",
            initialValue: item.Title, placeholder: item.Title);
        if (newTitle == null) return;

        var priorityChoice = await DisplayActionSheetAsync(
            "Priority", "Keep current", null,
            "High", "Medium", "Low");
        var newPriority = priorityChoice?.ToLowerInvariant() switch
        {
            "high"   => "high",
            "medium" => "medium",
            "low"    => "low",
            _        => item.Priority,
        };

        var currentDueLabel = item.HasDueDate ? item.DueDateLabel : "None";
        var dueDateChoice = await DisplayActionSheetAsync(
            $"Due date (current: {currentDueLabel})", "Keep current", null,
            "Today", "Tomorrow", "This week", "Remove due date");
        DateTime? newDueDate = dueDateChoice switch
        {
            "Today"            => DateTime.Today,
            "Tomorrow"         => DateTime.Today.AddDays(1),
            "This week"        => DateTime.Today.AddDays(7 - (int)DateTime.Today.DayOfWeek),
            "Remove due date"  => (DateTime?)null,
            _                  => item.DueDateParsed,
        };

        var finalTitle = string.IsNullOrWhiteSpace(newTitle) ? item.Title : newTitle.Trim();
        await GetTaskService().UpdateTaskAsync(item.Id, finalTitle, newPriority ?? "medium", newDueDate);
        await LoadAsync();
    }

    // ── INotifyPropertyChanged ────────────────────────────────────────────

    public new event PropertyChangedEventHandler? PropertyChanged;
    protected new void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
