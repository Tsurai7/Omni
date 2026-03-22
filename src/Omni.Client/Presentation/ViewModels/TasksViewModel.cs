using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Omni.Client.Abstractions;
using Omni.Client.Models.Task;
using Omni.Client.Services;

namespace Omni.Client.Presentation.ViewModels;

public partial class TasksViewModel : ObservableObject
{
    private readonly ITaskService _taskService;
    private readonly LocalDatabaseService _localDb;
    private DateTime _lastLoaded = DateTime.MinValue;

    public bool IsDataStale(TimeSpan threshold) => DateTime.UtcNow - _lastLoaded > threshold;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TodoCount), nameof(TotalCount), nameof(ProgressSummary), nameof(HasNoTasks),
        nameof(ShowTodoSection), nameof(FilterTodoLabel), nameof(FilterTodoBg), nameof(FilterTodoFg))]
    private ObservableCollection<TaskDisplayItem> _todoTasks = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(InProgressCount), nameof(TotalCount), nameof(ProgressSummary), nameof(HasNoTasks),
        nameof(ShowInProgressSection), nameof(FilterActiveLabel), nameof(FilterActiveBg), nameof(FilterActiveFg))]
    private ObservableCollection<TaskDisplayItem> _inProgressTasks = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DoneCount), nameof(TotalCount), nameof(ProgressSummary), nameof(HasNoTasks),
        nameof(ShowDoneSection), nameof(ShowDoneToggle), nameof(ShowDoneItems), nameof(FilterDoneLabel),
        nameof(FilterDoneBg), nameof(FilterDoneFg), nameof(DoneToggleLabel))]
    private ObservableCollection<TaskDisplayItem> _doneTasks = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowTodoSection), nameof(ShowInProgressSection), nameof(ShowDoneSection),
        nameof(ShowDoneToggle), nameof(ShowDoneItems), nameof(FilterAllBg), nameof(FilterAllFg),
        nameof(FilterTodoBg), nameof(FilterTodoFg), nameof(FilterActiveBg), nameof(FilterActiveFg),
        nameof(FilterDoneBg), nameof(FilterDoneFg))]
    private string _selectedFilter = "All";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowDoneItems), nameof(DoneToggleLabel))]
    private bool _isDoneExpanded;

    private static readonly Color PillActiveBg   = Color.FromArgb("#2E8B5CF6");
    private static readonly Color PillActiveFg   = Color.FromArgb("#8B5CF6");
    private static readonly Color PillInactiveBg = Color.FromArgb("#16161B");
    private static readonly Color PillInactiveFg = Color.FromArgb("#8E8EA0");

    public int TodoCount       => TodoTasks.Count;
    public int InProgressCount => InProgressTasks.Count;
    public int DoneCount       => DoneTasks.Count;
    public int TotalCount      => TodoCount + InProgressCount + DoneCount;

    public string ProgressSummary => TotalCount == 0 ? "No tasks yet" : $"{DoneCount} of {TotalCount} done";
    public bool HasNoTasks => TotalCount == 0;

    public bool ShowTodoSection       => (SelectedFilter == "All" && TodoCount > 0) || SelectedFilter == "Todo";
    public bool ShowInProgressSection => (SelectedFilter == "All" && InProgressCount > 0) || SelectedFilter == "Active";
    public bool ShowDoneSection       => (SelectedFilter == "All" && DoneCount > 0) || SelectedFilter == "Done";
    public bool ShowDoneToggle        => ShowDoneSection && SelectedFilter == "All";
    public bool ShowDoneItems         => SelectedFilter == "Done" || (SelectedFilter == "All" && IsDoneExpanded);

    public string FilterTodoLabel   => TodoCount > 0       ? $"Todo ({TodoCount})"         : "Todo";
    public string FilterActiveLabel => InProgressCount > 0 ? $"Active ({InProgressCount})" : "Active";
    public string FilterDoneLabel   => DoneCount > 0       ? $"Done ({DoneCount})"         : "Done";

    public Color FilterAllBg    => SelectedFilter == "All"    ? PillActiveBg : PillInactiveBg;
    public Color FilterAllFg    => SelectedFilter == "All"    ? PillActiveFg : PillInactiveFg;
    public Color FilterTodoBg   => SelectedFilter == "Todo"   ? PillActiveBg : PillInactiveBg;
    public Color FilterTodoFg   => SelectedFilter == "Todo"   ? PillActiveFg : PillInactiveFg;
    public Color FilterActiveBg => SelectedFilter == "Active" ? PillActiveBg : PillInactiveBg;
    public Color FilterActiveFg => SelectedFilter == "Active" ? PillActiveFg : PillInactiveFg;
    public Color FilterDoneBg   => SelectedFilter == "Done"   ? PillActiveBg : PillInactiveBg;
    public Color FilterDoneFg   => SelectedFilter == "Done"   ? PillActiveFg : PillInactiveFg;

    public string DoneToggleLabel => IsDoneExpanded
        ? $"▴  Hide completed ({DoneCount})"
        : $"▾  Show completed ({DoneCount})";

    public TaskDisplayItem? DraggedTask { get; set; }

    public TasksViewModel(ITaskService taskService, LocalDatabaseService localDb)
    {
        _taskService = taskService;
        _localDb = localDb;
    }

    [RelayCommand]
    private void DragStart(TaskDisplayItem? item) => DraggedTask = item;

    [RelayCommand]
    private void FilterAll()    => SelectedFilter = "All";

    [RelayCommand]
    private void FilterTodo()   => SelectedFilter = "Todo";

    [RelayCommand]
    private void FilterActive() => SelectedFilter = "Active";

    [RelayCommand]
    private void FilterDone()
    {
        IsDoneExpanded = true;
        SelectedFilter = "Done";
    }

    [RelayCommand]
    private void ToggleDoneExpanded() => IsDoneExpanded = !IsDoneExpanded;

    [RelayCommand]
    public async Task LoadAsync()
    {
        try
        {
            var fromApi   = await _taskService.GetTasksAsync();
            var fromLocal = await _localDb.GetAllTasksAsync();

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
            _lastLoaded = DateTime.UtcNow;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"TasksViewModel.LoadAsync error: {ex}");
        }
    }

    [RelayCommand]
    public async Task CyclePriorityAsync(TaskDisplayItem? item)
    {
        if (item == null) return;
        var nextPriority = item.Priority?.ToLowerInvariant() switch
        {
            "low"    => "medium",
            "medium" => "high",
            _        => "low",
        };
        await _taskService.UpdateTaskAsync(item.Id, item.Title, nextPriority);
        await LoadAsync();
    }

    [RelayCommand]
    public async Task DeleteTaskAsync(TaskDisplayItem? item)
    {
        if (item == null) return;
        await _taskService.DeleteTaskAsync(item.Id);
        await LoadAsync();
    }

    public async Task MoveTaskAsync(TaskDisplayItem item, string newStatus)
    {
        var oldStatus = item.Status;
        if (string.Equals(oldStatus, newStatus, StringComparison.OrdinalIgnoreCase)) return;

        // Optimistic UI: move item between collections immediately
        RemoveFromCollection(item, oldStatus);
        item = item with { Status = newStatus };
        AddToCollection(item, newStatus);

        var success = await _taskService.UpdateStatusAsync(item.Id, newStatus);
        if (!success)
        {
            // Rollback
            RemoveFromCollection(item, newStatus);
            item = item with { Status = oldStatus };
            AddToCollection(item, oldStatus);
        }
    }

    private void RemoveFromCollection(TaskDisplayItem item, string status)
    {
        var col = StatusToCollection(status);
        var existing = col.FirstOrDefault(t => t.Id == item.Id);
        if (existing != null) col.Remove(existing);
    }

    private void AddToCollection(TaskDisplayItem item, string status)
    {
        var col = StatusToCollection(status);
        col.Insert(0, item);
    }

    private ObservableCollection<TaskDisplayItem> StatusToCollection(string status) =>
        status?.ToLowerInvariant() switch
        {
            "in_progress" => InProgressTasks,
            "done"        => DoneTasks,
            _             => TodoTasks,
        };

    public async Task CreateTaskAsync(string title, string priority = "medium", DateTime? dueDate = null)
    {
        await _taskService.CreateTaskAsync(title.Trim(), priority, dueDate);
        await LoadAsync();
    }

    public async Task EditTaskAsync(TaskDisplayItem item, string newTitle, string newPriority, DateTime? newDueDate)
    {
        var finalTitle = string.IsNullOrWhiteSpace(newTitle) ? item.Title : newTitle.Trim();
        await _taskService.UpdateTaskAsync(item.Id, finalTitle, newPriority, newDueDate);
        await LoadAsync();
    }
}
