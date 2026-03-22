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
        if (_vm.IsDataStale(TimeSpan.FromSeconds(60)))
            _ = _vm.LoadAsync();
    }

    // ── Status move buttons ───────────────────────────────────────────────

    // "→ In Progress" (from To Do) or "✓ Done" (from In Progress)
    private async void OnMoveForwardClicked(object? sender, EventArgs e)
    {
        if (sender is not BindableObject bo || bo.BindingContext is not TaskDisplayItem item) return;
        var newStatus = item.Status?.ToLowerInvariant() switch
        {
            "pending"     => "in_progress",
            "in_progress" => "done",
            _             => null,
        };
        if (newStatus != null)
            await _vm.MoveTaskAsync(item, newStatus);
    }

    // "← To Do" (from In Progress) or "↩ In Progress" (from Done)
    private async void OnMoveBackClicked(object? sender, EventArgs e)
    {
        if (sender is not BindableObject bo || bo.BindingContext is not TaskDisplayItem item) return;
        var newStatus = item.Status?.ToLowerInvariant() switch
        {
            "in_progress" => "pending",
            "done"        => "in_progress",
            _             => null,
        };
        if (newStatus != null)
            await _vm.MoveTaskAsync(item, newStatus);
    }

    // ── Tap card: edit / move / delete ───────────────────────────────────

    private async void OnCardTapped(object? sender, EventArgs e)
    {
        if (sender is not BindableObject bo || bo.BindingContext is not TaskDisplayItem item) return;

        var action = await DisplayActionSheetAsync(item.Title, "Cancel", "Delete",
            "Edit", "Move to…");
        switch (action)
        {
            case "Edit":
                await EditTaskAsync(item);
                break;
            case "Move to…":
                await PickAndMoveStatusAsync(item);
                break;
            case "Delete":
                var confirm = await DisplayAlertAsync("Delete task", $"Remove \"{item.Title}\"?", "Delete", "Cancel");
                if (confirm) await _vm.DeleteTaskAsync(item);
                break;
        }
    }

    private async Task PickAndMoveStatusAsync(TaskDisplayItem item)
    {
        var choice = await DisplayActionSheetAsync("Move to", "Cancel", null,
            "To Do", "In Progress", "Done");
        var newStatus = choice switch
        {
            "To Do"       => "pending",
            "In Progress" => "in_progress",
            "Done"        => "done",
            _             => (string?)null,
        };
        if (newStatus != null)
            await _vm.MoveTaskAsync(item, newStatus);
    }

    // ── Add task ─────────────────────────────────────────────────────────

    private async void OnAddTaskClicked(object? sender, EventArgs e)
    {
        var title = await DisplayPromptAsync(
            "Add task", "What do you want to do?", "Add", "Cancel",
            placeholder: "e.g. Finish report");
        if (string.IsNullOrWhiteSpace(title)) return;

        var priority = await DisplayActionSheetAsync("Priority", "Cancel", null,
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

    // ── Edit ─────────────────────────────────────────────────────────────

    private async Task EditTaskAsync(TaskDisplayItem item)
    {
        var newTitle = await DisplayPromptAsync(
            "Edit task", "Update the task title:", "Save", "Cancel",
            initialValue: item.Title, placeholder: item.Title);
        if (newTitle == null) return;

        var priorityChoice = await DisplayActionSheetAsync(
            "Priority", "Keep current", null, "High", "Medium", "Low");
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
