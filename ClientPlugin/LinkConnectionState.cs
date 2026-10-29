using System;

namespace Lanyard;

internal sealed class LinkConnectionState
{
    public ulong AccountId;
    public long IdentityId;
    public long BlockId;
    public string Definition;
    public long OwnerId;
    public string DummyName;
    public Guid OriginWorldId;
    public bool Paused;
    public CombatResumeState Resume = new CombatResumeState();
}
