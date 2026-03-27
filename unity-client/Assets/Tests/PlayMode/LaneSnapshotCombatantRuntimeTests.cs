using System;
using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

public class LaneSnapshotCombatantRuntimeTests
{
    [UnityTest]
    public IEnumerator ApplySnapshot_Revives_LocallyDefeated_WaveUnit_When_ServerStillReports_Hp()
    {
        var go = new GameObject("LaneSnapshotCombatant_Test");
        Type combatantType = FindType("CastleDefender.Game.LaneSnapshotCombatant");
        Assert.That(combatantType, Is.Not.Null, "LaneSnapshotCombatant should be discoverable at runtime.");

        var combatant = go.AddComponent(combatantType);

        try
        {
            Invoke(combatant, "Initialize", "wave_test", "validation_wave", null, "red", 20f, 20f, 0.4f);
            Assert.That(ReadBool(combatant, "IsAlive"), Is.True, "Initialized combatants should begin alive.");

            Invoke(combatant, "ReceiveDamage", 999f, null);
            Assert.That(ReadBool(combatant, "IsLocallyDefeated"), Is.True, "Local shared-combat defeats should mark the unit as defeated.");
            Assert.That(go.activeSelf, Is.False, "Locally defeated wave units should hide until the next authoritative snapshot.");

            Invoke(combatant, "ApplySnapshot", "red", 14f, 20f, 0.4f);
            Assert.That(ReadBool(combatant, "IsLocallyDefeated"), Is.False, "Authoritative snapshots should be able to revive units that were only locally defeated.");
            Assert.That(ReadFloat(combatant, "CurrentHp"), Is.EqualTo(14f).Within(0.01f), "Snapshot HP should win over stale local prediction.");

            go.SetActive(true);
            yield return null;

            Assert.That(go.activeSelf, Is.True, "Revived wave units should be renderable again once the host view reactivates them.");
            Assert.That(ReadBool(combatant, "IsAlive"), Is.True, "Revived wave units should re-enter active combat after reconciliation.");
        }
        finally
        {
            if (go != null)
                UnityEngine.Object.DestroyImmediate(go);
        }
    }

    static Type FindType(string fullName)
    {
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type type = assembly.GetType(fullName, throwOnError: false);
            if (type != null)
                return type;
        }

        return null;
    }

    static void Invoke(Component component, string methodName, params object[] args)
    {
        MethodInfo method = component.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public);
        Assert.That(method, Is.Not.Null, $"{component.GetType().FullName} should expose method '{methodName}'.");
        method.Invoke(component, args);
    }

    static bool ReadBool(Component component, string propertyName)
    {
        return (bool)ReadProperty(component, propertyName);
    }

    static float ReadFloat(Component component, string propertyName)
    {
        return Convert.ToSingle(ReadProperty(component, propertyName));
    }

    static object ReadProperty(Component component, string propertyName)
    {
        PropertyInfo property = component.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public);
        Assert.That(property, Is.Not.Null, $"{component.GetType().FullName} should expose property '{propertyName}'.");
        return property.GetValue(component);
    }
}
