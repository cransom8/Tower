using System.Collections.Generic;
using UnityEngine;

namespace CastleDefender.Game
{
    public interface ILaneCombatant
    {
        string CombatantId { get; }
        Vector3 CombatPosition { get; }
        bool IsAlive { get; }
        float CurrentHp { get; }
        float MaxHp { get; }
        float AttackRange { get; }
        float EngagementRange { get; }
        bool IsEnemyTo(ILaneCombatant other);
        void ReceiveDamage(float damage, ILaneCombatant attacker);
    }

    public interface IWaveHostileLaneCombatant
    {
        bool IsEnemyToTeam(BattleTeam team);
        string DefenderTeamKey { get; }
    }

    public static class LaneCombatRegistry
    {
        static readonly List<ILaneCombatant> ActiveCombatants = new();

        public static IReadOnlyList<ILaneCombatant> Active
        {
            get
            {
                PruneDestroyedCombatants();
                return ActiveCombatants;
            }
        }

        public static void Register(ILaneCombatant combatant)
        {
            PruneDestroyedCombatants();
            if (IsNullOrDestroyed(combatant) || ActiveCombatants.Contains(combatant))
                return;

            ActiveCombatants.Add(combatant);
        }

        public static void Unregister(ILaneCombatant combatant)
        {
            if (IsNullOrDestroyed(combatant))
            {
                PruneDestroyedCombatants();
                return;
            }

            ActiveCombatants.Remove(combatant);
        }

        static void PruneDestroyedCombatants()
        {
            for (int i = ActiveCombatants.Count - 1; i >= 0; i--)
            {
                if (IsNullOrDestroyed(ActiveCombatants[i]))
                    ActiveCombatants.RemoveAt(i);
            }
        }

        static bool IsNullOrDestroyed(ILaneCombatant combatant)
        {
            if (combatant == null)
                return true;

            return combatant is Object unityObject && unityObject == null;
        }
    }
}
