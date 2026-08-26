using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using MiFuturoTrabajo.Infrastructure.Reminders;

namespace MiFuturoTrabajo.Functions
{
    /// <summary>
    /// The two scheduled reminder jobs.
    ///
    /// These are thin on purpose: bind a schedule, call
    /// <see cref="ReminderService"/>, log what happened. All the behaviour that
    /// is worth testing lives in Infrastructure, where a test can drive it
    /// without a Functions host.
    ///
    /// Both schedules come from app settings rather than being baked into the
    /// attribute, so the cadence can be tuned - or a job paused by pointing it
    /// at a far-future expression - without a redeploy.
    /// </summary>
    public class ReminderFunctions
    {
        private readonly ReminderService _reminders;
        private readonly ILogger<ReminderFunctions> _logger;

        public ReminderFunctions(ReminderService reminders, ILogger<ReminderFunctions> logger)
        {
            _reminders = reminders;
            _logger = logger;
        }

        /// <summary>
        /// Daily at 08:00 UTC. Tasks have day-scale deadlines, so a morning
        /// digest is the useful shape; polling hourly would only produce the
        /// same message earlier and more often.
        /// </summary>
        [Function(nameof(TaskReminders))]
        public async Task TaskReminders(
            [TimerTrigger("%TaskReminderSchedule%")] TimerInfo timer,
            CancellationToken cancellationToken)
        {
            var run = await _reminders.SendTaskRemindersAsync(DateTime.UtcNow, cancellationToken);

            Report("task", run, timer);
        }

        /// <summary>
        /// Every 15 minutes. The lead time is per meeting and can be as short as
        /// a few minutes, so the poll has to be finer-grained than the reminder
        /// it delivers.
        /// </summary>
        [Function(nameof(InterviewReminders))]
        public async Task InterviewReminders(
            [TimerTrigger("%InterviewReminderSchedule%")] TimerInfo timer,
            CancellationToken cancellationToken)
        {
            var run = await _reminders.SendInterviewRemindersAsync(DateTime.UtcNow, cancellationToken);

            Report("interview", run, timer);
        }

        private void Report(string kind, ReminderRun run, TimerInfo timer)
        {
            if (run.Considered == 0)
            {
                _logger.LogInformation("No {Kind} reminders due. Next run {Next}.", kind, timer.ScheduleStatus?.Next);
                return;
            }

            _logger.LogInformation(
                "{Kind} reminders: {Considered} item(s) due, {Sent} message(s) to {Recipients} recipient(s). Next run {Next}.",
                kind, run.Considered, run.Sent, run.Recipients, timer.ScheduleStatus?.Next);
        }
    }
}
