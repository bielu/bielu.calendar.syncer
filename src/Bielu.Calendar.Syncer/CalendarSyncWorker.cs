using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Bielu.Calendar.Syncer;

internal sealed partial class CalendarSyncWorker(
    ICalendarSyncer syncer,
    ISyncStatusTracker tracker,
    IOptions<CalendarSyncerOptions> options,
    TimeProvider timeProvider,
    ILogger<CalendarSyncWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = options.Value.Interval;
        LogWorkerStarted(interval);

        using var timer = new PeriodicTimer(interval, timeProvider);
        do
        {
            try
            {
                await syncer.SyncAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                LogScheduledSyncFailed(exception);
            }

            tracker.NextRunAt = timeProvider.GetUtcNow() + interval;
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Calendar sync worker started; running every {Interval}")]
    private partial void LogWorkerStarted(TimeSpan interval);

    [LoggerMessage(Level = LogLevel.Error, Message = "Scheduled sync failed")]
    private partial void LogScheduledSyncFailed(Exception exception);
}
