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

    [UnityTest]
    public IEnumerator EngagementRing_Stays_At_ProfileRadius_Even_When_UnitPresenter_Is_Scaled()
    {
        var go = new GameObject("LaneSnapshotCombatant_RingScale_Test");
        go.transform.localScale = Vector3.one * 2f;

        Type combatantType = FindType("CastleDefender.Game.LaneSnapshotCombatant");
        Assert.That(combatantType, Is.Not.Null, "LaneSnapshotCombatant should be discoverable at runtime.");

        GameObject body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        body.name = "Body";
        body.transform.SetParent(go.transform, false);

        Collider collider = body.GetComponent<Collider>();
        if (collider != null)
            UnityEngine.Object.DestroyImmediate(collider);

        var combatant = go.AddComponent(combatantType);

        try
        {
            Invoke(combatant, "Initialize", "ring_test", "validation_wave", null, "red", 20f, 20f, 0.4f);
            yield return null;

            float expectedRadius = ReadFloat(combatant, "EngagementRange");
            LineRenderer line = go.GetComponentInChildren<LineRenderer>(true);
            Assert.That(line, Is.Not.Null, "Snapshot combatants should create a visible engagement ring.");

            Vector3[] points = new Vector3[line.positionCount];
            int copied = line.GetPositions(points);
            Assert.That(copied, Is.EqualTo(line.positionCount), "The engagement ring should fully populate its line geometry.");

            float totalRadius = 0f;
            for (int i = 0; i < copied; i++)
            {
                Vector3 worldPoint = line.transform.TransformPoint(points[i]);
                Vector3 delta = worldPoint - line.transform.position;
                delta.y = 0f;
                totalRadius += delta.magnitude;
            }

            float averageRadius = totalRadius / Mathf.Max(1, copied);
            Assert.That(
                averageRadius,
                Is.EqualTo(expectedRadius).Within(0.1f),
                "The engagement ring should render at the unit's combat radius instead of being distorted by presenter scale.");
        }
        finally
        {
            if (go != null)
                UnityEngine.Object.DestroyImmediate(go);
        }
    }

    [UnityTest]
    public IEnumerator EngagementRing_Can_Be_Disabled_And_Reenabled_At_Runtime()
    {
        var go = new GameObject("LaneSnapshotCombatant_RingToggle_Test");
        Type combatantType = FindType("CastleDefender.Game.LaneSnapshotCombatant");
        Assert.That(combatantType, Is.Not.Null, "LaneSnapshotCombatant should be discoverable at runtime.");

        GameObject body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        body.name = "Body";
        body.transform.SetParent(go.transform, false);

        Collider collider = body.GetComponent<Collider>();
        if (collider != null)
            UnityEngine.Object.DestroyImmediate(collider);

        var combatant = go.AddComponent(combatantType);

        try
        {
            InvokeStatic(combatantType, "SetEngagementRingDebugEnabled", true);
            Invoke(combatant, "Initialize", "ring_toggle_test", "validation_wave", null, "red", 20f, 20f, 0.4f);
            yield return null;

            LineRenderer line = go.GetComponentInChildren<LineRenderer>(true);
            Assert.That(line, Is.Not.Null, "Snapshot combatants should expose a toggleable engagement ring.");
            Assert.That(line.enabled, Is.True, "The engagement ring should start enabled for combat debugging.");

            InvokeStatic(combatantType, "SetEngagementRingDebugEnabled", false);
            yield return null;
            Assert.That(line.enabled, Is.False, "Disabling engagement rings should hide them immediately on active combatants.");

            InvokeStatic(combatantType, "SetEngagementRingDebugEnabled", true);
            yield return null;
            Assert.That(line.enabled, Is.True, "Re-enabling engagement rings should restore them on active combatants.");
        }
        finally
        {
            InvokeStatic(combatantType, "SetEngagementRingDebugEnabled", true);
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

    static void InvokeStatic(Type type, string methodName, params object[] args)
    {
        MethodInfo method = type.GetMethod(methodName, BindingFlags.Static | BindingFlags.Public);
        Assert.That(method, Is.Not.Null, $"{type.FullName} should expose static method '{methodName}'.");
        method.Invoke(null, args);
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
