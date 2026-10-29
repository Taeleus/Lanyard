namespace Lanyard;

internal enum ConnectorDamageSource
{
    NativeDamage,
    ReplicatedIntegrity,
    WeaponCore
}

// A hit can appear in several observation paths. Only repeated evidence from
// the same path establishes repeated connector damage.
internal sealed class ConnectorDamageWindow
{
    private readonly long windowTicks;
    private readonly long[] previous = new long[3];
    private readonly bool[] observed = new bool[3];

    public ConnectorDamageWindow(long windowTicks) { this.windowTicks = windowTicks; }

    public bool Observe(ConnectorDamageSource source, long timestamp)
    {
        int index = (int)source;
        bool repeated = observed[index] && timestamp >= previous[index] &&
            timestamp - previous[index] <= windowTicks;
        observed[index] = true;
        previous[index] = timestamp;
        return repeated;
    }
}
