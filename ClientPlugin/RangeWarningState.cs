using System;

namespace Lanyard;

/// <summary>One quiet range warning per excursion, with hysteresis and a time limit.</summary>
internal sealed class RangeWarningState
{
    private readonly long frequency;
    private bool armed = true;
    private long lastWarning;
    private bool hasWarned;

    public RangeWarningState(long frequency) { this.frequency = frequency; }
    public void Reset() { armed = true; }
    public static bool IsWarningRange(double distance) => !double.IsNaN(distance) && !double.IsInfinity(distance) &&
        distance >= 80 && distance < LinkArea.BreakDistance;

    public bool Observe(double distance, long now, bool gameplayReady)
    {
        if (double.IsNaN(distance) || double.IsInfinity(distance)) return false;
        if (distance <= 70) armed = true;
        if (!armed || !IsWarningRange(distance) || !gameplayReady ||
            (hasWarned && now - lastWarning < 15L * frequency)) return false;
        armed = false;
        lastWarning = now;
        hasWarned = true;
        return true;
    }
}
