namespace Lanyard;

// Uses monotonic timestamps supplied by the caller; wall-clock changes cannot
// shorten an active cooldown. Teardown persists its remaining duration before
// clearing this live clock; a new session reconstructs a fresh deadline.
internal sealed class CombatCooldown
{
    private readonly long duration;
    private long until;
    public long Deadline => until;

    public CombatCooldown(long duration) { this.duration = duration; }
    public void Observe(long timestamp) { Include(timestamp + duration); }
    public void Include(long deadline) { if (deadline > until) until = deadline; }
    public bool IsBlocked(long timestamp) { return timestamp < until; }
    public void Clear() { until = 0; }
}
