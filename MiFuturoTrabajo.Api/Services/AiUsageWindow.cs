namespace MiFuturoTrabajo.Api.Services
{
    /// <param name="PeriodStartUtc">When the allowance currently in force began.</param>
    /// <param name="Used">Calls spent inside it.</param>
    public readonly record struct AiUsageState(DateTime PeriodStartUtc, int Used);

    /// <summary>
    /// The renewing allowance behind the AI features.
    ///
    /// Every other quota in this app counts rows that exist, so it needs no
    /// clock: delete a task and the room comes back. This one counts calls that
    /// have already been paid for and cannot be given back, so without a reset
    /// the first user to spend their allowance would lose the feature
    /// permanently.
    ///
    /// Periods roll forward from the moment of use rather than sitting on
    /// calendar boundaries. Aligning to the first of the month would mean
    /// someone registering on the 30th gets a full month's allowance for one
    /// day, and it puts every account's renewal on the same instant - a
    /// self-inflicted monthly spike on a feature whose whole design constraint
    /// is cost.
    ///
    /// Pure and clock-free: <paramref name="nowUtc"/> is passed in, so the
    /// rollover is testable without waiting a month for it.
    /// </summary>
    public static class AiUsageWindow
    {
        /// <summary>
        /// The state as it stands at <paramref name="nowUtc"/>, having applied
        /// any rollover that is due. Callers persist what comes back.
        /// </summary>
        public static AiUsageState Current(AiUsageState stored, TimeSpan period, DateTime nowUtc)
        {
            // Never used, or a document written before this feature existed.
            // Either way the allowance starts now rather than at the epoch,
            // which would otherwise read as one very overdue period.
            if (stored.PeriodStartUtc == default)
            {
                return new AiUsageState(nowUtc, 0);
            }

            // A clock that has gone backwards - a corrected host clock, or a
            // document written by a host that was ahead. Treating it as "the
            // period has not started yet" would hand out an unlimited
            // allowance, so it restarts instead.
            if (stored.PeriodStartUtc > nowUtc)
            {
                return new AiUsageState(nowUtc, 0);
            }

            return nowUtc - stored.PeriodStartUtc >= period
                ? new AiUsageState(nowUtc, 0)
                : stored;
        }

        public static DateTime RenewsAt(AiUsageState state, TimeSpan period) =>
            state.PeriodStartUtc + period;

        public static bool HasRoom(AiUsageState state, int limit) => state.Used < limit;

        public static int Remaining(AiUsageState state, int limit) =>
            Math.Max(0, limit - state.Used);
    }
}
