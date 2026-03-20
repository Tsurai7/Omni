using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
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
    private bool _isRefreshing;
    private ObservableCollection<TaskDisplayItem> _tasks = new();

    public TasksPage()
    {
        InitializeComponent();
        BindingContext = this;
        RefreshCommand = new Command(async () => await LoadAsync());
        MarkDoneCommand = new Command<TaskDisplayItem?>(async item =>
        {
            if (item != null)
            {
                await GetTaskService().UpdateStatusAsync(item.Id, "done");
                await LoadAsync();
            }
        });
        DeleteTaskCommand = new Command<TaskDisplayItem?>(async item =>
        {
            if (item == null) return;
            var confirm = await DisplayAlertAsync("Delete task", $"Remove \"{item.Title}\"?", "Delete", "Cancel");
            if (!confirm) return;
            await GetTaskService().DeleteTaskAsync(item.Id);
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

    public ICommand RefreshCommand { get; }
    public ICommand MarkDoneCommand { get; }
    public ICommand DeleteTaskCommand { get; }

    public bool IsRefreshing
    {
        get => _isRefreshing;
        set { if (_isRefreshing != value) { _isRefreshing = value; OnPropertyChanged(); } }
    }

    public ObservableCollection<TaskDisplayItem> Tasks
    {
        get => _tasks;
        set { _tasks = value; OnPropertyChanged(); }
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
        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        IsRefreshing = true;
        try
        {
            var taskService = GetTaskService();
            var localDb = GetLocalDb();

            var fromApi = await taskService.GetTasksAsync();
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

            merged = merged.OrderByDescending(x =>
            {
                if (DateTime.TryParse(x.CreatedAt, out var dt)) return dt;
                return DateTime.MinValue;
            }).ToList();

            Tasks = new ObservableCollection<TaskDisplayItem>(merged);
        }
        finally
        {
            IsRefreshing = false;
        }
    }

    private async void OnAddTaskClicked(object? sender, EventArgs e)
    {
        var title = await DisplayPromptAsync("Add task", "What do you want to do?", "Add", "Cancel", placeholder: "e.g. Finish report");
        if (string.IsNullOrWhiteSpace(title)) return;

        await GetTaskService().CreateTaskAsync(title.Trim());
        await LoadAsync();
    }

    public new event PropertyChangedEventHandler? PropertyChanged;
    protected new void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
