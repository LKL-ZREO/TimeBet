using System.Globalization;
using TimeBet.Application.Abstractions;
using TimeBet.Domain;
using TimeBet.Domain.Tasks;

namespace TimeBet.Application.Tasks;

public sealed class PlannerService
{
    private readonly IPlanningTimeBlockRepository blocks;
    private readonly IPlanningTaskRepository tasks;
    private readonly IClock clock;
    private readonly TimeZoneInfo timeZone;

    public PlannerService(
        IPlanningTimeBlockRepository blocks,
        IPlanningTaskRepository tasks,
        IClock clock,
        TimeZoneInfo? timeZone = null)
    {
        this.blocks = blocks;
        this.tasks = tasks;
        this.clock = clock;
        this.timeZone = timeZone ?? TimeZoneInfo.Local;
    }

    public async Task<TodayPlanSnapshot> GetTodayAsync(CancellationToken cancellationToken = default)
    {
        var nowUtc = clock.UtcNow.ToUniversalTime();
        var localDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(nowUtc, timeZone).Date);
        var dayStartUtc = StartOfDayUtc(localDate);
        var dayEndUtc = StartOfDayUtc(localDate.AddDays(1));
        var timeBlocks = await blocks.GetInRangeAsync(dayStartUtc, dayEndUtc, cancellationToken);
        var taskMap = new Dictionary<Guid, PlanningTask>();
        foreach (var taskId in timeBlocks.Select(block => block.TaskId).Distinct())
        {
            var task = await tasks.GetAsync(taskId, cancellationToken);
            if (task is not null)
            {
                taskMap[taskId] = task;
            }
        }

        var snapshots = timeBlocks
            .Where(block => taskMap.ContainsKey(block.TaskId))
            .Select(block => Map(block, taskMap[block.TaskId]))
            .ToArray();

        return new TodayPlanSnapshot(
            localDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            dayStartUtc,
            dayEndUtc,
            snapshots.Sum(block => (long)block.PlannedSeconds),
            snapshots.Sum(block => block.ActualSeconds),
            snapshots.Count(block => block.Status == "planned"),
            snapshots.Count(block => block.Status == "completed"),
            snapshots.Count(block => block.Status == "skipped"),
            snapshots);
    }

    public async Task<WeekPlanSnapshot> GetWeekAsync(CancellationToken cancellationToken = default)
    {
        var nowUtc = clock.UtcNow.ToUniversalTime();
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(nowUtc, timeZone).Date);
        var weekStartDate = today.AddDays(-DaysSinceMonday(today.DayOfWeek));
        var weekStartUtc = StartOfDayUtc(weekStartDate);
        var weekEndUtc = StartOfDayUtc(weekStartDate.AddDays(7));
        var timeBlocks = await blocks.GetInRangeAsync(weekStartUtc, weekEndUtc, cancellationToken);
        var snapshots = await MapBlocksAsync(timeBlocks, cancellationToken);
        var days = Enumerable.Range(0, 7)
            .Select(index => BuildDay(weekStartDate.AddDays(index), snapshots))
            .ToArray();

        return new WeekPlanSnapshot(
            weekStartDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            weekStartUtc,
            weekEndUtc,
            days.Sum(day => day.PlannedSeconds),
            days.Sum(day => day.ActualSeconds),
            days.Sum(day => day.CompletedCount),
            days.Sum(day => day.SkippedCount),
            days);
    }

    public async Task<PlanningTimeBlockSnapshot> CreateAsync(
        CreateTimeBlockRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.PlannedMinutes is <= 0 or > 480)
        {
            throw new ArgumentOutOfRangeException(
                nameof(request),
                "A time block must be between 1 minute and 8 hours.");
        }

        var task = await tasks.GetAsync(request.TaskId, cancellationToken)
            ?? throw new DomainRuleException("Task does not exist.");
        if (task.Status != PlanningTaskStatus.Active)
        {
            throw new DomainRuleException("Only active tasks can be scheduled.");
        }

        var block = PlanningTimeBlock.Create(
            task.Id,
            request.StartsAtUtc,
            checked(request.PlannedMinutes * 60),
            clock.UtcNow);
        if (await blocks.HasOverlapAsync(
                block.StartsAtUtc,
                block.StartsAtUtc.AddSeconds(block.PlannedSeconds),
                cancellationToken: cancellationToken))
        {
            throw new DomainRuleException("This time block overlaps another active plan.");
        }

        await blocks.AddAsync(block, cancellationToken);
        return Map(block, task);
    }

    public async Task<PlanningTimeBlockSnapshot> MoveAsync(
        MoveTimeBlockRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.PlannedMinutes is <= 0 or > 480)
        {
            throw new ArgumentOutOfRangeException(
                nameof(request),
                "A time block must be between 1 minute and 8 hours.");
        }

        var block = await blocks.GetAsync(request.Id, cancellationToken)
            ?? throw new DomainRuleException("Time block does not exist.");
        if (block.Status != PlanningTimeBlockStatus.Planned)
        {
            throw new DomainRuleException("Only planned time blocks can be moved.");
        }

        var startsAtUtc = request.StartsAtUtc.ToUniversalTime();
        var plannedSeconds = checked(request.PlannedMinutes * 60);
        if (await blocks.HasOverlapAsync(
                startsAtUtc,
                startsAtUtc.AddSeconds(plannedSeconds),
                block.Id,
                cancellationToken))
        {
            throw new DomainRuleException("This time block overlaps another active plan.");
        }

        await blocks.MoveAsync(block.Id, startsAtUtc, plannedSeconds, cancellationToken);
        var task = await tasks.GetAsync(block.TaskId, cancellationToken)
            ?? throw new DomainRuleException("Linked task does not exist.");
        return Map(block with
        {
            StartsAtUtc = startsAtUtc,
            PlannedSeconds = plannedSeconds,
        }, task);
    }

    public async Task<PlanningTimeBlockSnapshot> DeferAsync(
        DeferTimeBlockRequest request,
        CancellationToken cancellationToken = default)
    {
        var block = await blocks.GetAsync(request.Id, cancellationToken)
            ?? throw new DomainRuleException("Time block does not exist.");
        var localStart = TimeZoneInfo.ConvertTime(block.StartsAtUtc, timeZone);
        var nextLocalStart = DateTime.SpecifyKind(localStart.DateTime.AddDays(1), DateTimeKind.Unspecified);
        var nextStartUtc = TimeZoneInfo.ConvertTimeToUtc(nextLocalStart, timeZone);
        return await MoveAsync(
            new MoveTimeBlockRequest(
                block.Id,
                nextStartUtc,
                block.PlannedSeconds / 60),
            cancellationToken);
    }

    public async Task<PlanningTimeBlockSnapshot> SkipAsync(
        SkipTimeBlockRequest request,
        CancellationToken cancellationToken = default)
    {
        var block = await blocks.GetAsync(request.Id, cancellationToken)
            ?? throw new DomainRuleException("Time block does not exist.");
        if (block.Status != PlanningTimeBlockStatus.Planned)
        {
            throw new DomainRuleException("Only planned time blocks can be skipped.");
        }

        await blocks.SkipAsync(block.Id, clock.UtcNow, cancellationToken);
        var task = await tasks.GetAsync(block.TaskId, cancellationToken)
            ?? throw new DomainRuleException("Linked task does not exist.");
        return Map(block with
        {
            Status = PlanningTimeBlockStatus.Skipped,
            ResolvedAtUtc = clock.UtcNow,
        }, task);
    }

    private DateTimeOffset StartOfDayUtc(DateOnly date)
    {
        var local = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        return TimeZoneInfo.ConvertTimeToUtc(local, timeZone);
    }

    private async Task<PlanningTimeBlockSnapshot[]> MapBlocksAsync(
        IReadOnlyList<PlanningTimeBlock> timeBlocks,
        CancellationToken cancellationToken)
    {
        var taskMap = new Dictionary<Guid, PlanningTask>();
        foreach (var taskId in timeBlocks.Select(block => block.TaskId).Distinct())
        {
            var task = await tasks.GetAsync(taskId, cancellationToken);
            if (task is not null)
            {
                taskMap[taskId] = task;
            }
        }

        return timeBlocks
            .Where(block => taskMap.ContainsKey(block.TaskId))
            .Select(block => Map(block, taskMap[block.TaskId]))
            .ToArray();
    }

    private PlanDaySnapshot BuildDay(
        DateOnly date,
        IReadOnlyList<PlanningTimeBlockSnapshot> timeBlocks)
    {
        var dayStartUtc = StartOfDayUtc(date);
        var dayEndUtc = StartOfDayUtc(date.AddDays(1));
        var dayBlocks = timeBlocks
            .Where(block => block.StartsAtUtc >= dayStartUtc && block.StartsAtUtc < dayEndUtc)
            .OrderBy(block => block.StartsAtUtc)
            .ToArray();
        return new PlanDaySnapshot(
            date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            dayStartUtc,
            dayEndUtc,
            dayBlocks.Sum(block => (long)block.PlannedSeconds),
            dayBlocks.Sum(block => block.ActualSeconds),
            dayBlocks.Count(block => block.Status == "planned"),
            dayBlocks.Count(block => block.Status == "completed"),
            dayBlocks.Count(block => block.Status == "skipped"),
            dayBlocks);
    }

    private static int DaysSinceMonday(DayOfWeek dayOfWeek)
    {
        return ((int)dayOfWeek - (int)DayOfWeek.Monday + 7) % 7;
    }

    private static PlanningTimeBlockSnapshot Map(PlanningTimeBlock block, PlanningTask task)
    {
        return new PlanningTimeBlockSnapshot(
            block.Id,
            task.Id,
            task.Title,
            task.DueDate,
            block.StartsAtUtc,
            block.PlannedSeconds,
            block.ActualSeconds,
            block.Status.ToString().ToLowerInvariant(),
            block.ResolvedAtUtc);
    }
}
