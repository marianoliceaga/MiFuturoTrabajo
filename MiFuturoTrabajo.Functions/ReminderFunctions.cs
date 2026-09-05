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
        /// Every three hours.
        ///
        /// A meeting is selectable only between the moment its window opens and
        /// the moment it starts, so at this cadence a lead time shorter than
        /// three hours - the entity default is 60 minutes - would fall between
        /// two runs and never be sent at all.
        ///
        /// <see cref="ReminderOptions.InterviewPollIntervalMinutes"/> tells the
        /// selection how far apart these runs are, so it sends at the last run
        /// before the window opens instead. Such a reminder arrives up to three
        /// hours early. That setting has to be changed in step with the cron.
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
