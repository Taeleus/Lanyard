namespace Lanyard;

internal sealed class CombatResumeState
{
    public const int MaxIds = 4096;
    public long[] GridIds = new long[0];
    public long[] WeaponIds = new long[0];
}
