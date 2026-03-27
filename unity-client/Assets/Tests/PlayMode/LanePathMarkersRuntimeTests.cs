using System;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

public class LanePathMarkersRuntimeTests
{
    Type lanePathMarkersType;

    [SetUp]
    public void SetUp()
    {
        lanePathMarkersType = FindType("CastleDefender.Game.LanePathMarkers");
        Assert.That(lanePathMarkersType, Is.Not.Null, "LanePathMarkers should be discoverable at runtime.");
        InvokeStaticNoArgs("DebugClearRegistry");
    }

    [TearDown]
    public void TearDown()
    {
        if (lanePathMarkersType != null)
            InvokeStaticNoArgs("DebugClearRegistry");
    }

    [Test]
    public void TrySampleDetailed_Fails_When_LaneRegistrationIsAmbiguous()
    {
        var root = new GameObject("LanePathMarkersRuntimeTests_Ambiguous");
        try
        {
            CreateMarkers(root.transform, "LaneA", 0, "left_branch_a", Vector3.zero);
            CreateMarkers(root.transform, "LaneB", 0, "left_branch_a_duplicate", new Vector3(10f, 0f, 0f));

            object[] args = { 0, null, 0.5f, null, null, null };
            bool ok = InvokeTrySampleDetailed(args);
            string failureReason = args[5] as string;

            Assert.That(ok, Is.False);
            Assert.That(failureReason, Does.Contain("Ambiguous"));
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(root);
        }
    }

    [Test]
    public void TrySampleDetailed_Fails_When_ExplicitMarkersAreMissing()
    {
        var root = new GameObject("LanePathMarkersRuntimeTests_Missing");
        try
        {
            Component markers = CreateMarkers(root.transform, "LaneMissing", 1, "left_branch_b", Vector3.zero);
            Array markerTransforms = (Array)lanePathMarkersType.GetField("MarkerTransforms", BindingFlags.Instance | BindingFlags.Public)
                .GetValue(markers);
            markerTransforms.SetValue(null, 4);

            object[] args = { 1, "left_branch_b", 0.25f, null, null, null };
            bool ok = InvokeTrySampleDetailed(args);
            string failureReason = args[5] as string;

            Assert.That(ok, Is.False);
            Assert.That(failureReason, Does.Contain("missing Marker_5"));
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(root);
        }
    }

    [Test]
    public void TrySampleRoute_Falls_Back_To_UniqueLaneRegistration_When_PathKeys_Are_Missing()
    {
        var root = new GameObject("LanePathMarkersRuntimeTests_Fallback");
        try
        {
            CreateMarkers(root.transform, "LaneFallback", 0, null, new Vector3(2f, 0f, 4f));

            object[] args = { 0, "left_branch_a", "left_a", 0f, null, null, null, null, null };
            bool ok = InvokeTrySampleRoute(args);

            Assert.That(ok, Is.True);
            Assert.That((string)args[6], Is.EqualTo("lane:0"));
            Assert.That((bool)args[7], Is.True);
            Assert.That(Vector3.Distance((Vector3)args[4], new Vector3(2f, 0f, 4f)), Is.LessThanOrEqualTo(0.001f));
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(root);
        }
    }

    Component CreateMarkers(Transform parent, string name, int laneIndex, string pathKey, Vector3 origin)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.position = origin;

        Component markers = go.AddComponent(lanePathMarkersType);
        lanePathMarkersType.GetField("LaneIndex", BindingFlags.Instance | BindingFlags.Public).SetValue(markers, laneIndex);
        lanePathMarkersType.GetField("PathKey", BindingFlags.Instance | BindingFlags.Public).SetValue(markers, pathKey);

        var center = new GameObject("CenterMarker").transform;
        center.SetParent(go.transform, false);
        center.position = origin;
        lanePathMarkersType.GetField("CenterMarker", BindingFlags.Instance | BindingFlags.Public).SetValue(markers, center);

        var markerTransforms = Array.CreateInstance(typeof(Transform), 5);
        for (int i = 0; i < markerTransforms.Length; i++)
        {
            var marker = new GameObject($"Marker_{i + 1}").transform;
            marker.SetParent(go.transform, false);
            marker.position = origin + new Vector3(0f, 0f, (i + 1) * 2f);
            markerTransforms.SetValue(marker, i);
        }

        lanePathMarkersType.GetField("MarkerTransforms", BindingFlags.Instance | BindingFlags.Public).SetValue(markers, markerTransforms);
        ((Behaviour)markers).enabled = false;
        ((Behaviour)markers).enabled = true;
        return markers;
    }

    bool InvokeTrySampleDetailed(object[] args)
    {
        MethodInfo method = lanePathMarkersType.GetMethod("TrySampleDetailed", BindingFlags.Public | BindingFlags.Static);
        Assert.That(method, Is.Not.Null, "LanePathMarkers should expose TrySampleDetailed.");
        return (bool)method.Invoke(null, args);
    }

    bool InvokeTrySampleRoute(object[] args)
    {
        MethodInfo method = lanePathMarkersType.GetMethod("TrySampleRoute", BindingFlags.Public | BindingFlags.Static);
        Assert.That(method, Is.Not.Null, "LanePathMarkers should expose TrySampleRoute.");
        return (bool)method.Invoke(null, args);
    }

    void InvokeStaticNoArgs(string methodName)
    {
        MethodInfo method = lanePathMarkersType.GetMethod(methodName, BindingFlags.Public | BindingFlags.Static);
        if (method != null)
            method.Invoke(null, null);
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
}
