using Microsoft.EntityFrameworkCore;

using Prdb.Ordeno.Core.History;
using Prdb.Ordeno.Infrastructure.Persistence;

namespace Prdb.Ordeno.Infrastructure.History;

/// <summary>
/// The log as the screen reads it: runs, newest first, each with the entries it
/// wrote.
/// </summary>
/// <remarks>
/// Paged rather than capped, like the review queue and unlike the filing screen:
/// this is a record somebody scrolls back through until they find the night they
/// are looking for. What is capped is how many entries one run sends
/// (<see cref="HistoryLimits.EntriesShown"/>) — a first pass over a library is
/// thousands, the account carries the scale, and the rows carry the examples.
/// </remarks>
public sealed class HistoryService(OrdenoDbContext context)
{
    public async Task<OperationHistory> ReadAsync(int page, CancellationToken cancellationToken = default)
    {
        var total = await context.OperationRuns.CountAsync(cancellationToken);

        if (total == 0)
        {
            return OperationHistory.Nothing;
        }

        var pages = Math.Max(1, (total + HistoryLimits.PageSize - 1) / HistoryLimits.PageSize);
        var wanted = Math.Clamp(page, 1, pages);

        var runs = await context.OperationRuns
            .AsNoTracking()
            .OrderByDescending(run => run.Id)
            .Skip((wanted - 1) * HistoryLimits.PageSize)
            .Take(HistoryLimits.PageSize)
            .ToListAsync(cancellationToken);

        var ids = runs.Select(run => run.Id).ToList();

        var counted = await context.Operations
            .AsNoTracking()
            .Where(entry => ids.Contains(entry.RunId))
            .GroupBy(entry => entry.RunId)
            .Select(group => new
            {
                RunId = group.Key,
                Operations = group.Count(),
                Undone = group.Count(entry => entry.UndoneAt != null),
            })
            .ToListAsync(cancellationToken);

        var read = new List<LoggedRun>(runs.Count);

        foreach (var run in runs)
        {
            var counts = counted.FirstOrDefault(count => count.RunId == run.Id);

            // One query per run on the page rather than one for all of them: the
            // limit is per run, and a run of four thousand entries must not push
            // the other nineteen off the answer.
            var entries = await context.Operations
                .AsNoTracking()
                .Where(entry => entry.RunId == run.Id)
                .OrderByDescending(entry => entry.Id)
                .Take(HistoryLimits.EntriesShown)
                .ToListAsync(cancellationToken);

            read.Add(new LoggedRun(
                run.Id,
                run.Kind,
                run.AskedBy,
                run.StartedAt,
                run.FinishedAt,
                run.Account,
                run.Problem,
                counts?.Operations ?? 0,
                counts?.Undone ?? 0,
                [.. entries.Select(UndoService.Read)]));
        }

        return new OperationHistory(read, wanted, pages, total);
    }
}
