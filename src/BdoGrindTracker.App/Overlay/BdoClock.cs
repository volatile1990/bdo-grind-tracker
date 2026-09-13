namespace BdoGrindTracker.App.Overlay;

public sealed record BdoClockState(TimeSpan GameTime, bool IsDaytime, TimeSpan UntilNextChange);

/// <summary>The regular NA/EU world cycle, independent of local timezone and DST.</summary>
public static class BdoClock
{
    // Cycle and UTC anchor verified against the published bdo-clock implementation:
    // https://github.com/markni/bdo-clock/blob/master/bdo-clock.js
    // 00:20 UTC is 07:00 in-game; daytime takes 200 real minutes, night 40.
    // An offset can calibrate a server with a different cycle; special areas and
    // events which force a particular time of day do not follow this world clock.
    private const long CycleTicks = 240 * TimeSpan.TicksPerMinute;
    private const long DayTicks = 200 * TimeSpan.TicksPerMinute;
    private const long NightTicks = 40 * TimeSpan.TicksPerMinute;
    private const long DawnTicks = 20 * TimeSpan.TicksPerMinute;

    public static BdoClockState At(DateTimeOffset now, int offsetMinutes = 0)
    {
        var ticks = now.UtcTicks - DateTimeOffset.UnixEpoch.UtcTicks - DawnTicks +
                    Math.Clamp(offsetMinutes, -240, 240) * TimeSpan.TicksPerMinute;
        var phase = ((ticks % CycleTicks) + CycleTicks) % CycleTicks;
        var isDaytime = phase < DayTicks;
        // Integer ratios avoid rounding errors at sunrise and sunset. The game
        // clock advances 4.5 times as fast by day and 13.5 times as fast at night.
        var gameTicks = isDaytime
            ? 7 * TimeSpan.TicksPerHour + phase * 9 / 2
            : 22 * TimeSpan.TicksPerHour + (phase - DayTicks) * 27 / 2;
        return new(TimeSpan.FromTicks(gameTicks % TimeSpan.TicksPerDay), isDaytime,
            TimeSpan.FromTicks(isDaytime ? DayTicks - phase : DayTicks + NightTicks - phase));
    }
}
