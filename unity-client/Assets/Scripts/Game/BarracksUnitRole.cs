namespace CastleDefender.Game
{
    // Retained because live roster/presentation systems still classify barracks units
    // by role even though the legacy barracks spawning runtime has been archived.
    public enum BarracksUnitRole
    {
        Frontline = 0,
        Ranged = 1,
        Support = 2,
        Siege = 3,
    }
}
