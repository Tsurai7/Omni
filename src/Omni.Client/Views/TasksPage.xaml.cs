using Omni.Client.Abstractions;
using Omni.Client.Models.Task;
using Omni.Client.Presentation.ViewModels;

namespace Omni.Client;

public partial class TasksPage : ContentPage
{
    private readonly TasksViewModel _vm;

    public TasksPage(TasksViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        BindingContext = vm;
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
        await _vm.LoadAsync();
    }

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
            "Pick date" => DateTime.Today.AddDays(7),
            _           => null,
        };

        await _vm.CreateTaskAsync(title.Trim(), p, dueDate);
    }

    private async void OnEditTaskRequested(object? sender, EventArgs e)
    {
        if (sender is BindableObject bo && bo.BindingContext is TaskDisplayItem item)
            await EditTaskAsync(item);
    }

    private async void OnDeleteTaskRequested(object? sender, EventArgs e)
    {
        if (sender is BindableObject bo && bo.BindingContext is TaskDisplayItem item)
        {
            var confirm = await DisplayAlertAsync("Delete task", $"Remove \"{item.Title}\"?", "Delete", "Cancel");
            if (!confirm) return;
            await _vm.DeleteTaskAsync(item);
        }
    }

    private async void OnCyclePriorityRequested(object? sender, EventArgs e)
    {
        if (sender is BindableObject bo && bo.BindingContext is TaskDisplayItem item)
            await _vm.CyclePriorityAsync(item);
    }

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
            "Today"           => DateTime.Today,
            "Tomorrow"        => DateTime.Today.AddDays(1),
            "This week"       => DateTime.Today.AddDays(7 - (int)DateTime.Today.DayOfWeek),
            "Remove due date" => (DateTime?)null,
            _                 => item.DueDateParsed,
        };

        await _vm.EditTaskAsync(item, newTitle, newPriority ?? "medium", newDueDate);
    }
}
