using Omni.Client.Models.Session;
using Omni.Client.Models.Task;
using Omni.Client.Services;
using Xunit;

namespace Omni.Client.Tests;

// ── TaskDisplayItem ─────────────────────────────────────────────────────────

public sealed class TaskDisplayItemTests
{
    private static TaskDisplayItem Make(
        string status   = "pending",
        string priority = "medium",
        string? dueDate = null) =>
        new("id", "u", "Test task", status, priority,
            "2024-01-01", "2024-01-01", dueDate);

    // Status helpers
    [Theory]
    [InlineData("pending",     true,  false, false)]
    [InlineData("PENDING",     true,  false, false)]
    [InlineData("in_progress", false, true,  false)]
    [InlineData("done",        false, false, true)]
    [InlineData("Done",        false, false, true)]
    public void StatusProperties_ReflectCorrectStatus(
        string status, bool isPending, bool isInProgress, bool isDone)
    {
        var item = Make(status);
        Assert.Equal(isPending,    item.IsPending);
        Assert.Equal(isInProgress, item.IsInProgress);
        Assert.Equal(isDone,       item.IsDone);
    }

    // Priority label
    [Theory]
    [InlineData("high",   "HIGH")]
    [InlineData("medium", "MED")]
    [InlineData("low",    "LOW")]
    [InlineData("HIGH",   "HIGH")]
    public void PriorityLabel_ReturnsCorrectLabel(string priority, string expected)
    {
        Assert.Equal(expected, Make(priority: priority).PriorityLabel);
    }

    // Due date label
    [Fact]
    public void DueDateLabel_NoDueDate_ReturnsEmptyString()
    {
        Assert.Equal("", Make().DueDateLabel);
    }

    [Fact]
    public void DueDateLabel_Today_ReturnsToday()
    {
        var item = Make(dueDate: DateTime.Today.ToString("O"));
        Assert.Equal("Today", item.DueDateLabel);
    }

    [Fact]
    public void DueDateLabel_Tomorrow_ReturnsTomorrow()
    {
        var item = Make(dueDate: DateTime.Today.AddDays(1).ToString("O"));
        Assert.Equal("Tomorrow", item.DueDateLabel);
    }

    [Fact]
    public void DueDateLabel_Yesterday_ReturnsYesterday()
    {
        var item = Make(dueDate: DateTime.Today.AddDays(-1).ToString("O"));
        Assert.Equal("Yesterday", item.DueDateLabel);
    }

    [Fact]
    public void DueDateLabel_OtherDate_ReturnsFormattedDate()
    {
        var date = new DateTime(2024, 6, 15);
        var item = Make(dueDate: date.ToString("O"));
        Assert.Equal("Jun 15", item.DueDateLabel);
    }

    // IsOverdue
    [Fact]
    public void IsOverdue_PastDueDatePendingTask_ReturnsTrue()
    {
        var item = Make(status: "pending", dueDate: DateTime.Today.AddDays(-1).ToString("O"));
        Assert.True(item.IsOverdue);
    }

    [Fact]
    public void IsOverdue_PastDueDateDoneTask_ReturnsFalse()
    {
        var item = Make(status: "done", dueDate: DateTime.Today.AddDays(-1).ToString("O"));
        Assert.False(item.IsOverdue); // done tasks are never overdue
    }

    [Fact]
    public void IsOverdue_FutureDueDate_ReturnsFalse()
    {
        var item = Make(status: "pending", dueDate: DateTime.Today.AddDays(1).ToString("O"));
        Assert.False(item.IsOverdue);
    }

    [Fact]
    public void IsOverdue_NoDueDate_ReturnsFalse()
    {
        Assert.False(Make(status: "pending").IsOverdue);
    }

    // HasDueDate
    [Fact]
    public void HasDueDate_WithDate_ReturnsTrue()
    {
        Assert.True(Make(dueDate: DateTime.Today.ToString("O")).HasDueDate);
    }

    [Fact]
    public void HasDueDate_WithoutDate_ReturnsFalse()
    {
        Assert.False(Make().HasDueDate);
    }

    // Factory methods
    [Fact]
    public void FromListItem_MapsAllFields()
    {
        var item = new TaskListItem("id1", "u1", "Buy milk", "in_progress", "high",
            "2024-01-01", "2024-01-02", "2024-12-31");
        var display = TaskDisplayItem.FromListItem(item);

        Assert.Equal("id1",         display.Id);
        Assert.Equal("Buy milk",    display.Title);
        Assert.Equal("in_progress", display.Status);
        Assert.Equal("high",        display.Priority);
        Assert.True(display.HasDueDate);
    }

    [Fact]
    public void FromLocalTask_MapsAllFields()
    {
        var task = new LocalTask
        {
            Id       = "local-1",
            Title    = "Write tests",
            Status   = "pending",
            Priority = "low",
            IsSynced = false,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        var display = TaskDisplayItem.FromLocalTask(task);

        Assert.Equal("local-1",    display.Id);
        Assert.Equal("Write tests", display.Title);
        Assert.Equal("pending",     display.Status);
        Assert.Equal("low",         display.Priority);
    }
}

// ── SessionDisplayItem ──────────────────────────────────────────────────────

public sealed class SessionDisplayItemTests
{
    [Fact]
    public void FromEntry_ShortDuration_FormatsAsMMSS()
    {
        var entry = new SessionListEntry("s1", "Quick session", "work",
            "2024-01-15T09:00:00Z", DurationSeconds: 5 * 60 + 30); // 5m 30s
        var display = SessionDisplayItem.FromEntry(entry);
        Assert.Equal("05:30", display.DurationDisplay);
    }

    [Fact]
    public void FromEntry_LongDuration_FormatsWithHours()
    {
        var entry = new SessionListEntry("s2", "Deep work", "work",
            "2024-01-15T09:00:00Z", DurationSeconds: 2 * 3600 + 15 * 60 + 5); // 2h 15m 5s
        var display = SessionDisplayItem.FromEntry(entry);
        Assert.Equal("2:15:05", display.DurationDisplay);
    }

    [Fact]
    public void FromEntry_StartedAtDisplayedAsHHMM()
    {
        var entry = new SessionListEntry("s3", "Coding", "work",
            "2024-01-15T14:35:00Z", DurationSeconds: 3600);
        var display = SessionDisplayItem.FromEntry(entry);
        // Local time depends on timezone; we just check it has HH:MM format
        Assert.Matches(@"^\d{2}:\d{2}$", display.StartedAtDisplay);
    }

    [Fact]
    public void FromEntry_InvalidStartedAt_StartedAtDisplayIsEmpty()
    {
        var entry = new SessionListEntry("s4", "Session", "work",
            "", DurationSeconds: 600);
        var display = SessionDisplayItem.FromEntry(entry);
        Assert.Equal("", display.StartedAtDisplay);
    }

    [Fact]
    public void FromEntry_MapsNameAndActivityType()
    {
        var entry = new SessionListEntry("s5", "Gym workout", "exercise",
            "2024-01-15T07:00:00Z", DurationSeconds: 3600);
        var display = SessionDisplayItem.FromEntry(entry);
        Assert.Equal("Gym workout", display.Name);
        Assert.Equal("exercise",    display.ActivityType);
    }
}

// ── SessionScoreCalculator ──────────────────────────────────────────────────

public sealed class SessionScoreCalculatorTests
{
    [Fact]
    public void Calculate_FullyFocused_Returns100()
    {
        var score = SessionScoreCalculator.Calculate(3600, 0, 0, 5);
        Assert.Equal(100, score);
    }

    [Fact]
    public void Calculate_HalfDistracted_NoEvents_Returns50()
    {
        var score = SessionScoreCalculator.Calculate(3600, 1800, 0, 5);
        Assert.Equal(50, score);
    }

    [Fact]
    public void Calculate_FullyFocusedWithTwoEvents_DeductsEventPenalty()
    {
        // 100% focused - 2 events × 5 pts = 90
        var score = SessionScoreCalculator.Calculate(3600, 0, 2, 5);
        Assert.Equal(90, score);
    }

    [Fact]
    public void Calculate_HeavyPenalty_ClampsToZero()
    {
        // 50% focus - 20 events × 5 pts = 50 - 100 → clamped to 0
        var score = SessionScoreCalculator.Calculate(3600, 1800, 20, 5);
        Assert.Equal(0, score);
    }

    [Fact]
    public void Calculate_ZeroTotalSeconds_Returns100()
    {
        var score = SessionScoreCalculator.Calculate(0, 0, 0, 5);
        Assert.Equal(100, score);
    }

    [Fact]
    public void Calculate_NegativeTotalSeconds_Returns100()
    {
        var score = SessionScoreCalculator.Calculate(-1, 0, 0, 5);
        Assert.Equal(100, score);
    }

    [Fact]
    public void Calculate_AllDistracted_Returns0MinusPenalty_ClampedTo0()
    {
        var score = SessionScoreCalculator.Calculate(3600, 3600, 0, 5);
        Assert.Equal(0, score);
    }

    [Theory]
    [InlineData(3600, 0,    0, 5,  100)] // perfect focus
    [InlineData(3600, 900,  0, 5,   75)] // 25% distracted, no events
    [InlineData(3600, 900,  3, 5,   60)] // 25% distracted, 3 events × 5 = 75 - 15 = 60
    [InlineData(3600, 1800, 1, 10,  40)] // 50% distracted, 1 event × 10 = 50 - 10 = 40
    public void Calculate_VariousInputs_ProducesExpectedScore(
        double total, double distracted, int events, int penalty, int expected)
    {
        Assert.Equal(expected, SessionScoreCalculator.Calculate(total, distracted, events, penalty));
    }
}
