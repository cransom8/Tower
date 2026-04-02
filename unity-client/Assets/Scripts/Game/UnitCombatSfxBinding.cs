using UnityEngine;

namespace CastleDefender.Game
{
    public enum UnitCombatSfxCue
    {
        Spawn = 0,
        Attack = 1,
        Impact = 2,
        Defend = 3,
        Hurt = 4,
        Death = 5,
    }

    [DisallowMultipleComponent]
    public sealed class UnitCombatSfxBinding : MonoBehaviour
    {
        [Header("Profile")]
        public string profileKey = "light_melee";
        public bool enableImpactCue;
        public bool enableDefendCue;

        [Header("Chance Overrides")]
        [Range(0f, 1f)] public float spawnChance = 0.28f;
        [Range(0f, 1f)] public float attackChance = 0.18f;
        [Range(0f, 1f)] public float impactChance = 0.16f;
        [Range(0f, 1f)] public float defendChance = 0.14f;
        [Range(0f, 1f)] public float hurtChance = 0.14f;
        [Range(0f, 1f)] public float deathChance = 0.82f;

        [TextArea(2, 5)]
        public string notes;

        public bool AllowsCue(UnitCombatSfxCue cue) => cue switch
        {
            UnitCombatSfxCue.Impact => enableImpactCue,
            UnitCombatSfxCue.Defend => enableDefendCue,
            _ => true,
        };

        public float ResolveChance(UnitCombatSfxCue cue) => cue switch
        {
            UnitCombatSfxCue.Spawn => spawnChance,
            UnitCombatSfxCue.Attack => attackChance,
            UnitCombatSfxCue.Impact => impactChance,
            UnitCombatSfxCue.Defend => defendChance,
            UnitCombatSfxCue.Hurt => hurtChance,
            UnitCombatSfxCue.Death => deathChance,
            _ => 0f,
        };
    }
}
