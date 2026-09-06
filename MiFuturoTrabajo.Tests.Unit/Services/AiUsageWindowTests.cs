using MiFuturoTrabajo.Api.Services;
using Xunit;

namespace MiFuturoTrabajo.Tests.Unit.Services
{
    /// <summary>
    /// The renewing allowance. Worth covering properly because it is the only
    /// thing standing between an open registration form and an unbounded bill,
    /// and because the interesting cases - a period elapsing, a clock moving
    /// backwards - are ones nobody would notice by using the app.
    /// </summary>
    public class AiUsageWindowTests
    {
        private static readonly TimeSpan Period = TimeSpan.FromDays(30);
        private static readonly DateTime Now = new(2026, 6, 15, 12, 0, 0, DateTimeKind.Utc);

        [Fact]
        public void NeverUsed_StartsThePeriodNow()
        {
            var state = AiUsageWindow.Current(new AiUsageState(default, 0), Period, Now);

            Assert.Equal(Now, state.PeriodStartUtc);
            Assert.Equal(0, state.Used);
        }

        [Fact]
        public void NeverUsed_DoesNotReadAsOnePeriodOverdue()
        {
            // A document written before this feature existed has default(DateTime)
            // here. Treated as a real period start it would be centuries stale,
            // which happens to give the right answer - and would give the wrong
            // one the moment the count was non-zero.
            var state = AiUsageWindow.Current(new AiUsageState(default, 3), Period, Now);

            Assert.Equal(0, state.Used);
            Assert.Equal(Now, state.PeriodStartUtc);
        }

        [Fact]
        public void InsideThePeriod_KeepsTheCount()
        {
            var started = Now.AddDays(-10);

            var state = AiUsageWindow.Current(new AiUsageState(started, 3), Period, Now);

            Assert.Equal(started, state.PeriodStartUtc);
            Assert.Equal(3, state.Used);
        }

        [Fact]
        public void OnTheBoundary_Resets()
        {
            // Exactly 30 days is a new period, not the last moment of the old
            // one: the comparison is >=, and a test pinning that stops a later
            // "tidy-up" from turning it into > and adding a silent day.
            var state = AiUsageWindow.Current(new AiUsageState(Now - Period, 5), Period, Now);

            Assert.Equal(Now, state.PeriodStartUtc);
            Assert.Equal(0, state.Used);
        }

        [Fact]
        public void JustInsideTheBoundary_DoesNotReset()
        {
            var started = Now - Period + TimeSpan.FromSeconds(1);

            var state = AiUsageWindow.Current(new AiUsageState(started, 5), Period, Now);

            Assert.Equal(5, state.Used);
        }

        [Fact]
        public void AfterALongGap_TheNewPeriodStartsNow()
        {
            // Not at the end of some notional missed period: rolling forward
            // from a year ago would otherwise hand out a window that expires
            // immediately.
            var state = AiUsageWindow.Current(new AiUsageState(Now.AddYears(-1), 5), Period, Now);

            Assert.Equal(Now, state.PeriodStartUtc);
            Assert.Equal(Now + Period, AiUsageWindow.RenewsAt(state, Period));
        }

        [Fact]
        public void AFutureStart_Resets_RatherThanGrantingFreeCalls()
        {
            // A clock that has gone backwards. "The period has not begun yet"
            // would read as never expiring, which is the one failure mode that
            // costs money.
            var state = AiUsageWindow.Current(new AiUsageState(Now.AddDays(5), 5), Period, Now);

            Assert.Equal(Now, state.PeriodStartUtc);
            Assert.Equal(0, state.Used);
        }

        [Theory]
        [InlineData(0, true)]
        [InlineData(4, true)]
        [InlineData(5, false)]
        [InlineData(9, false)]
        public void HasRoom_IsExclusiveOfTheLimit(int used, bool expected)
        {
            var state = new AiUsageState(Now, used);

            Assert.Equal(expected, AiUsageWindow.HasRoom(state, 5));
        }

        [Fact]
        public void Remaining_NeverGoesNegative()
        {
            // A limit lowered by configuration leaves existing accounts over it.
            // "-3 remaining" would reach the UI and read as nonsense.
            Assert.Equal(0, AiUsageWindow.Remaining(new AiUsageState(Now, 8), 5));
        }
    }
}
