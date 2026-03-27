using System;
using System.Reflection;
using CastleDefender.Net;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

public class WaveSnapshotRuntimeSpawnerRuntimeTests
{
    [UnityTest]
    public System.Collections.IEnumerator WaveUnit_Materializes_From_Authoritative_BattlefieldRoute_State()
    {
        Type registryType = FindType("CastleDefender.Game.UnitPrefabRegistry");
        Type spawnerType = FindType("CastleDefender.Game.WaveSnapshotRuntimeSpawner");

        Assert.That(registryType, Is.Not.Null);
        Assert.That(spawnerType, Is.Not.Null);

        var root = new GameObject("WaveSnapshotRuntimeSpawnerRuntimeTests");
        ScriptableObject registry = null;
        GameObject prefab = null;

        try
        {
            GameObject waveCenter = CreateWaveCenterAnchor("Wave_Center_Anchor", new Vector3(0f, 0f, 0f));
            CreateFrontGateAnchor("Red_Gate_Front", new Vector3(-12f, 0f, 12f));

            registry = ScriptableObject.CreateInstance(registryType);
            prefab = CreateUnitPrefab();
            ConfigureSkinEntries(registry, prefab);
            InvokeNoArgs(registry, "Rebuild");

            var spawnerHost = new GameObject("WaveRuntime");
            spawnerHost.transform.SetParent(root.transform, false);
            var spawner = spawnerHost.AddComponent(spawnerType);
            spawnerType.GetField("Registry", BindingFlags.Instance | BindingFlags.Public).SetValue(spawner, registry);

            var snapshot = new MLSnapshot
            {
                lanes = new[]
                {
                    new MLLaneSnap
                    {
                        laneIndex = 0,
                        team = "red",
                        slotColor = "red",
                        slotKey = "left_a",
                        branchId = "left_branch_a",
                        fullPathLength = 54,
                        path = new[]
                        {
                            new MLGridPos { x = 5, y = 0 },
                            new MLGridPos { x = 5, y = 1 },
                        },
                        units = new[]
                        {
                            new MLUnit
                            {
                                id = "wu_runtime_1",
                                type = "goblin",
                                skinKey = "tt_peasant",
                                laneId = 0,
                                isWaveUnit = true,
                                spawnSourceType = "scheduled_wave",
                                routeType = "WAVE_LANE",
                                routeStartNode = "WA",
                                routeTargetNode = "A",
                                pathId = "WA_A",
                                currentWaypointIndex = 0,
                                nextWaypoint = "A",
                                currentSegment = "WA_A",
                                segmentProgress = 0f,
                                movementState = "MOVING",
                                routeWorldX = -24f,
                                routeWorldY = -1f,
                                hp = 10f,
                                maxHp = 10f,
                                moveSpeed = 1f,
                            }
                        }
                    }
                }
            };

            Invoke(spawner, "DebugApplySnapshot", snapshot);
            yield return null;

            Transform waveUnit = spawnerHost.transform.Find("SnapshotUnit_wu_runtime_1_goblin_tt_peasant");
            Vector3 expected = waveCenter.transform.position;
            Assert.That(waveUnit, Is.Not.Null, "Wave units should materialize from the authoritative battlefield route contract.");
            Assert.That(waveUnit.position.x, Is.EqualTo(expected.x).Within(0.01f));
            Assert.That(waveUnit.position.z, Is.EqualTo(expected.z).Within(0.01f));
        }
        finally
        {
            if (root != null)
                UnityEngine.Object.DestroyImmediate(root);
            if (prefab != null)
                UnityEngine.Object.DestroyImmediate(prefab);
            if (registry != null)
                UnityEngine.Object.DestroyImmediate(registry);
            DestroyIfPresent("Red_Gate_Front");
            DestroyIfPresent("Wave_Center_Anchor");
        }
    }

    [UnityTest]
    public System.Collections.IEnumerator CoreCombat_Uses_Live_Server_CombatSpace_Instead_Of_Stale_RouteData()
    {
        Type registryType = FindType("CastleDefender.Game.UnitPrefabRegistry");
        Type spawnerType = FindType("CastleDefender.Game.WaveSnapshotRuntimeSpawner");

        Assert.That(registryType, Is.Not.Null);
        Assert.That(spawnerType, Is.Not.Null);

        var root = new GameObject("WaveSnapshotRuntimeSpawnerRuntimeTests_RouteCoreTownCore");
        ScriptableObject registry = null;
        GameObject prefab = null;

        try
        {
            CreateWaveCenterAnchor("Wave_Center_Anchor", new Vector3(0f, 0f, 0f));
            CreateFrontGateAnchor("Red_Gate_Front", new Vector3(-12f, 0f, 12f));
            GameObject redTownCore = CreateTownCoreAnchor("Red_TownCore_Anchor", "Red", new Vector3(-20f, 0f, 20f));

            registry = ScriptableObject.CreateInstance(registryType);
            prefab = CreateUnitPrefab();
            ConfigureSkinEntries(registry, prefab);
            InvokeNoArgs(registry, "Rebuild");

            var spawnerHost = new GameObject("WaveRuntime_RouteCoreTownCore");
            spawnerHost.transform.SetParent(root.transform, false);
            var spawner = spawnerHost.AddComponent(spawnerType);
            spawnerType.GetField("Registry", BindingFlags.Instance | BindingFlags.Public).SetValue(spawner, registry);

            var snapshot = new MLSnapshot
            {
                lanes = new[]
                {
                    new MLLaneSnap
                    {
                        laneIndex = 0,
                        team = "red",
                        slotColor = "red",
                        slotKey = "left_a",
                        branchId = "left_branch_a",
                        units = new[]
                        {
                            new MLUnit
                            {
                                id = "town_core_combat_live_space",
                                type = "goblin",
                                skinKey = "tt_peasant",
                                laneId = 0,
                                isWaveUnit = true,
                                spawnSourceType = "scheduled_wave",
                                routeType = "WAVE_LANE",
                                routeStartNode = "WA",
                                routeTargetNode = "A",
                                pathId = "WA_A",
                                currentWaypointIndex = 0,
                                nextWaypoint = "A",
                                currentSegment = "WA_A",
                                segmentProgress = 1f,
                                movementState = "COMBAT",
                                state = "COMBAT",
                                gridX = -24f,
                                gridY = 19f,
                                routeWorldX = -24f,
                                routeWorldY = 24f,
                                blockedByStructure = true,
                                blockedByStructureId = "Red_TownCore_Anchor_pad",
                                combatTargetId = "Red_TownCore_Anchor_pad",
                                hp = 10f,
                                maxHp = 10f,
                                moveSpeed = 1f,
                            }
                        },
                        fortressPads = new[]
                        {
                            new MLFortressPad
                            {
                                padId = "Red_TownCore_Anchor_pad",
                                buildingType = "town_core",
                                hp = 20f,
                                maxHp = 20f,
                            }
                        },
                    }
                }
            };

            Invoke(spawner, "DebugApplySnapshot", snapshot);
            yield return null;

            Transform unit = spawnerHost.transform.Find("SnapshotUnit_town_core_combat_live_space_goblin_tt_peasant");
            Assert.That(unit, Is.Not.Null, "Units in core combat should still materialize through battlefield routes.");

            Vector3 expected = Vector3.Lerp(redTownCore.transform.position, new Vector3(-12f, 0f, 12f), 5f / 10.2f);
            Assert.That(unit.position.x, Is.EqualTo(expected.x).Within(0.05f));
            Assert.That(unit.position.z, Is.EqualTo(expected.z).Within(0.05f));
        }
        finally
        {
            if (root != null)
                UnityEngine.Object.DestroyImmediate(root);
            if (prefab != null)
                UnityEngine.Object.DestroyImmediate(prefab);
            if (registry != null)
                UnityEngine.Object.DestroyImmediate(registry);
            DestroyIfPresent("Red_Gate_Front");
            DestroyIfPresent("Red_TownCore_Anchor");
            DestroyIfPresent("Wave_Center_Anchor");
        }
    }

    [UnityTest]
    public System.Collections.IEnumerator DefenderUnit_Materializes_Through_Unified_SnapshotSpawner()
    {
        Type registryType = FindType("CastleDefender.Game.UnitPrefabRegistry");
        Type spawnerType = FindType("CastleDefender.Game.WaveSnapshotRuntimeSpawner");
        Type combatantType = FindType("CastleDefender.Game.LaneSnapshotCombatant");
        Type tileGridType = FindType("CastleDefender.Game.TileGrid");

        Assert.That(registryType, Is.Not.Null);
        Assert.That(spawnerType, Is.Not.Null);
        Assert.That(combatantType, Is.Not.Null);
        Assert.That(tileGridType, Is.Not.Null);

        var root = new GameObject("WaveSnapshotRuntimeSpawnerRuntimeTests_Defender");
        ScriptableObject registry = null;
        GameObject prefab = null;

        try
        {
            registry = ScriptableObject.CreateInstance(registryType);
            prefab = CreateUnitPrefab();
            ConfigureSkinEntries(registry, prefab);
            InvokeNoArgs(registry, "Rebuild");

            var spawnerHost = new GameObject("UnifiedSnapshotRuntime");
            spawnerHost.transform.SetParent(root.transform, false);
            var spawner = spawnerHost.AddComponent(spawnerType);
            spawnerType.GetField("Registry", BindingFlags.Instance | BindingFlags.Public).SetValue(spawner, registry);

            var snapshot = new MLSnapshot
            {
                lanes = new[]
                {
                    new MLLaneSnap
                    {
                        laneIndex = 0,
                        team = "red",
                        slotColor = "red",
                        units = new[]
                        {
                            new MLUnit
                            {
                                id = "def_runtime_1",
                                type = "tt_peasant",
                                skinKey = "tt_peasant",
                                isWaveUnit = false,
                                isDefender = true,
                                sourceTeam = "red",
                                gridX = 5f,
                                gridY = 6f,
                                hp = 12f,
                                maxHp = 12f,
                                moveSpeed = 1f,
                            }
                        }
                    }
                }
            };

            Invoke(spawner, "DebugApplySnapshot", snapshot);
            yield return null;

            Transform defenderUnit = spawnerHost.transform.Find("SnapshotUnit_def_runtime_1_tt_peasant_tt_peasant");
            Vector3 expected = InvokeStaticVector3(tileGridType, "TileToWorld", 0, 5f, 6f);
            Assert.That(defenderUnit, Is.Not.Null, "Defenders should materialize through the same snapshot-driven runtime spawner as attackers.");
            Assert.That(defenderUnit.position.x, Is.EqualTo(expected.x).Within(0.01f));
            Assert.That(defenderUnit.position.z, Is.EqualTo(expected.z).Within(0.01f));
            Assert.That(defenderUnit.GetComponent(combatantType), Is.Not.Null, "Unified snapshot units should register the shared LaneSnapshotCombatant.");
        }
        finally
        {
            if (root != null)
                UnityEngine.Object.DestroyImmediate(root);
            if (prefab != null)
                UnityEngine.Object.DestroyImmediate(prefab);
            if (registry != null)
                UnityEngine.Object.DestroyImmediate(registry);
            DestroyIfPresent("Red_TownCore_Anchor");
            DestroyIfPresent("Blue_TownCore_Anchor");
            DestroyIfPresent("Wave_Center_Anchor");
        }
    }

    [UnityTest]
    public System.Collections.IEnumerator AttackerUnits_Preserve_Authoritative_RouteSpacing_From_ServerRouteWorld()
    {
        Type registryType = FindType("CastleDefender.Game.UnitPrefabRegistry");
        Type spawnerType = FindType("CastleDefender.Game.WaveSnapshotRuntimeSpawner");

        Assert.That(registryType, Is.Not.Null);
        Assert.That(spawnerType, Is.Not.Null);

        var root = new GameObject("WaveSnapshotRuntimeSpawnerRuntimeTests_RouteSpacing");
        ScriptableObject registry = null;
        GameObject prefab = null;

        try
        {
            GameObject waveCenter = CreateWaveCenterAnchor("Wave_Center_Anchor", new Vector3(0f, 0f, 0f));
            GameObject redFrontGate = CreateFrontGateAnchor("Red_Gate_Front", new Vector3(0f, 0f, 24f));
            GameObject redCenterBarracks = CreateBarracksSiteAnchor("Red_Center_Barracks_Anchor", "Red", "center", new Vector3(-6f, 0f, 12f));

            registry = ScriptableObject.CreateInstance(registryType);
            prefab = CreateUnitPrefab();
            ConfigureSkinEntries(registry, prefab);
            InvokeNoArgs(registry, "Rebuild");

            var spawnerHost = new GameObject("AuthoritativeRouteRuntime");
            spawnerHost.transform.SetParent(root.transform, false);
            var spawner = spawnerHost.AddComponent(spawnerType);
            spawnerType.GetField("Registry", BindingFlags.Instance | BindingFlags.Public).SetValue(spawner, registry);

            var snapshot = new MLSnapshot
            {
                lanes = new[]
                {
                    new MLLaneSnap
                    {
                        laneIndex = 0,
                        team = "red",
                        slotColor = "red",
                        branchId = "left_branch_a",
                        units = Array.Empty<MLUnit>(),
                    },
                    new MLLaneSnap
                    {
                        laneIndex = 1,
                        team = "gold",
                        slotColor = "gold",
                        branchId = "left_branch_b",
                        units = new[]
                        {
                            new MLUnit
                            {
                                id = "route_spacing_a",
                                type = "infantry_t1",
                                archetypeKey = "infantry_t1",
                                presentationKey = "human_default",
                                laneId = 1,
                                barracksId = "center",
                                spawnSourceType = "barracks_roster",
                                isWaveUnit = false,
                                ownerLane = 0,
                                sourceLaneIndex = 0,
                                sourceTeam = "red",
                                sourceBarracksKey = "center",
                                sourceBarracksId = "center",
                                pathId = "ACTR_A>A_M>M_B",
                                currentWaypointIndex = 0,
                                nextWaypoint = "A",
                                routeType = "CENTER_CROSS",
                                routeStartNode = "ACTR",
                                routeTargetNode = "B",
                                currentSegment = "ACTR_A",
                                segmentProgress = 1f,
                                movementState = "MOVING",
                                routeWorldX = -24.6f,
                                routeWorldY = 24f,
                                hp = 15f,
                                maxHp = 15f,
                                moveSpeed = 1f,
                            },
                            new MLUnit
                            {
                                id = "route_spacing_b",
                                type = "infantry_t1",
                                archetypeKey = "infantry_t1",
                                presentationKey = "human_default",
                                laneId = 1,
                                barracksId = "center",
                                spawnSourceType = "barracks_roster",
                                isWaveUnit = false,
                                ownerLane = 0,
                                sourceLaneIndex = 0,
                                sourceTeam = "red",
                                sourceBarracksKey = "center",
                                sourceBarracksId = "center",
                                pathId = "ACTR_A>A_M>M_B",
                                currentWaypointIndex = 0,
                                nextWaypoint = "A",
                                routeType = "CENTER_CROSS",
                                routeStartNode = "ACTR",
                                routeTargetNode = "B",
                                currentSegment = "ACTR_A",
                                segmentProgress = 1f,
                                movementState = "MOVING",
                                routeWorldX = -23.4f,
                                routeWorldY = 24f,
                                hp = 15f,
                                maxHp = 15f,
                                moveSpeed = 1f,
                            }
                        }
                    }
                }
            };

            Invoke(spawner, "DebugApplySnapshot", snapshot);
            yield return null;

            Transform first = spawnerHost.transform.Find("SnapshotUnit_route_spacing_a_tt_peasant_tt_peasant");
            Transform second = spawnerHost.transform.Find("SnapshotUnit_route_spacing_b_tt_peasant_tt_peasant");
            Assert.That(first, Is.Not.Null);
            Assert.That(second, Is.Not.Null);

            float separation = Vector3.Distance(first.position, second.position);
            Assert.That(separation, Is.GreaterThan(0.9f), "Route-space offsets should survive Unity materialization instead of collapsing units into one presenter position.");
            Vector3 midpoint = (first.position + second.position) * 0.5f;
            Vector3 expectedGateStaging = Vector3.Lerp(redFrontGate.transform.position, waveCenter.transform.position, 0.5f);
            Assert.That(
                Vector3.Distance(midpoint, redCenterBarracks.transform.position),
                Is.GreaterThan(4.5f),
                "Barracks roster hold routing should stage outside the barracks instead of collapsing inside the center building.");
            Assert.That(
                Vector3.Distance(midpoint, expectedGateStaging),
                Is.LessThan(0.75f),
                "Route core staging should sit halfway between the front gate and the mine center.");
        }
        finally
        {
            if (root != null)
                UnityEngine.Object.DestroyImmediate(root);
            if (prefab != null)
                UnityEngine.Object.DestroyImmediate(prefab);
            if (registry != null)
                UnityEngine.Object.DestroyImmediate(registry);
            DestroyIfPresent("Wave_Center_Anchor");
            DestroyIfPresent("Red_Gate_Front");
            DestroyIfPresent("Red_Center_Barracks_Anchor");
        }
    }

    [UnityTest]
    public System.Collections.IEnumerator TwoPlayer_OuterLoop_Units_Materialize_Through_Unused_Travel_Lanes()
    {
        Type registryType = FindType("CastleDefender.Game.UnitPrefabRegistry");
        Type spawnerType = FindType("CastleDefender.Game.WaveSnapshotRuntimeSpawner");

        Assert.That(registryType, Is.Not.Null);
        Assert.That(spawnerType, Is.Not.Null);

        var root = new GameObject("WaveSnapshotRuntimeSpawnerRuntimeTests_TwoPlayerOuterLoop");
        ScriptableObject registry = null;
        GameObject prefab = null;

        try
        {
            CreateWaveCenterAnchor("Wave_Center_Anchor", new Vector3(0f, 0f, 0f));
            CreateFrontGateAnchor("Yellow_Gate_Front", new Vector3(12f, 0f, 12f));
            CreateFrontGateAnchor("Blue_Gate_Front", new Vector3(12f, 0f, -12f));

            registry = ScriptableObject.CreateInstance(registryType);
            prefab = CreateUnitPrefab();
            ConfigureSkinEntries(registry, prefab);
            InvokeNoArgs(registry, "Rebuild");

            var spawnerHost = new GameObject("TwoPlayerOuterLoopRuntime");
            spawnerHost.transform.SetParent(root.transform, false);
            var spawner = spawnerHost.AddComponent(spawnerType);
            spawnerType.GetField("Registry", BindingFlags.Instance | BindingFlags.Public).SetValue(spawner, registry);

            var snapshot = new MLSnapshot
            {
                lanes = new[]
                {
                    new MLLaneSnap
                    {
                        laneIndex = 0,
                        team = "red",
                        slotColor = "red",
                        branchId = "left_branch_a",
                        units = Array.Empty<MLUnit>(),
                    },
                    new MLLaneSnap
                    {
                        laneIndex = 1,
                        team = "gold",
                        slotColor = "gold",
                        branchId = "left_branch_b",
                        units = new[]
                        {
                            new MLUnit
                            {
                                id = "two_player_outer_loop_unit",
                                type = "infantry_t1",
                                archetypeKey = "infantry_t1",
                                presentationKey = "human_default",
                                laneId = 1,
                                barracksId = "right",
                                spawnSourceType = "barracks_roster",
                                isWaveUnit = false,
                                ownerLane = 0,
                                sourceLaneIndex = 0,
                                sourceTeam = "red",
                                sourceBarracksKey = "right",
                                sourceBarracksId = "right",
                                pathId = "ARGT_A>A_B>B_C>C_D>D_A",
                                currentWaypointIndex = 2,
                                nextWaypoint = "C",
                                routeType = "OUTER_LOOP",
                                routeStartNode = "ARGT",
                                routeTargetNode = "B",
                                currentSegment = "B_C",
                                segmentProgress = 0.5f,
                                movementState = "MOVING",
                                routeWorldX = 28f,
                                routeWorldY = 0f,
                                hp = 15f,
                                maxHp = 15f,
                                moveSpeed = 1f,
                            }
                        }
                    }
                }
            };

            Invoke(spawner, "DebugApplySnapshot", snapshot);
            yield return null;

            Transform unit = spawnerHost.transform.Find("SnapshotUnit_two_player_outer_loop_unit_tt_peasant_tt_peasant");
            Assert.That(
                unit,
                Is.Not.Null,
                "Two-player outer-loop barracks units should remain materialized when they traverse the blue/green travel-space nodes.");
        }
        finally
        {
            if (root != null)
                UnityEngine.Object.DestroyImmediate(root);
            if (prefab != null)
                UnityEngine.Object.DestroyImmediate(prefab);
            if (registry != null)
                UnityEngine.Object.DestroyImmediate(registry);
            DestroyIfPresent("Yellow_Gate_Front");
            DestroyIfPresent("Blue_Gate_Front");
            DestroyIfPresent("Wave_Center_Anchor");
        }
    }

    static void ConfigureSkinEntries(ScriptableObject registry, GameObject prefab)
    {
        Type registryType = registry.GetType();
        Type skinEntryType = registryType.GetNestedType("SkinEntry", BindingFlags.Public | BindingFlags.NonPublic);
        Array entries = Array.CreateInstance(skinEntryType, 1);
        entries.SetValue(CreateSkinEntry(skinEntryType, "tt_peasant", "tt_peasant", prefab), 0);
        registryType.GetField("skinEntries", BindingFlags.Instance | BindingFlags.Public).SetValue(registry, entries);
    }

    static object CreateSkinEntry(Type skinEntryType, string skinKey, string unitType, GameObject prefab)
    {
        object entry = Activator.CreateInstance(skinEntryType);
        SetField(entry, "skinKey", skinKey);
        SetField(entry, "unitType", unitType);
        SetField(entry, "prefab", prefab);
        SetField(entry, "scale", 1f);
        return entry;
    }

    static GameObject CreateUnitPrefab()
    {
        var prefab = new GameObject("ValidationWaveUnitPrefab");
        var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        body.name = "Body";
        body.transform.SetParent(prefab.transform, false);

        Collider collider = body.GetComponent<Collider>();
        if (collider != null)
            UnityEngine.Object.DestroyImmediate(collider);

        return prefab;
    }

    static void DestroyIfPresent(string objectName)
    {
        GameObject existing = GameObject.Find(objectName);
        if (existing != null)
            UnityEngine.Object.DestroyImmediate(existing);
    }

    static GameObject CreateTownCoreAnchor(string name, string laneColorName, Vector3 position)
    {
        Type anchorType = FindType("CastleDefender.Game.FortressPadAnchor");
        Assert.That(anchorType, Is.Not.Null, "FortressPadAnchor should be discoverable at runtime.");

        var go = new GameObject(name);
        go.transform.position = position;
        go.AddComponent<BoxCollider>();
        Component anchor = go.AddComponent(anchorType);
        SetField(anchor, "padId", $"{name}_pad");
        SetField(anchor, "buildingType", "town_core");
        SetEnumField(anchor, "laneColor", laneColorName);
        return go;
    }

    static GameObject CreateFrontGateAnchor(string name, Vector3 position)
    {
        var go = new GameObject(name);
        go.transform.position = position;
        return go;
    }

    static GameObject CreateWaveCenterAnchor(string name, Vector3 position)
    {
        Type anchorType = FindType("CastleDefender.Game.WaveSpawnAnchor");
        Assert.That(anchorType, Is.Not.Null, "WaveSpawnAnchor should be discoverable at runtime.");

        var go = new GameObject(name);
        go.transform.position = position;
        Component anchor = go.AddComponent(anchorType);
        SetPrivateField(anchor, "anchorId", "mine_center");
        SetPrivateField(anchor, "focusTransform", go.transform);
        return go;
    }

    static GameObject CreateBarracksSiteAnchor(string name, string laneColorName, string barracksId, Vector3 position)
    {
        Type anchorType = FindType("CastleDefender.Game.BarracksSiteView");
        Assert.That(anchorType, Is.Not.Null, "BarracksSiteView should be discoverable at runtime.");

        var go = new GameObject(name);
        go.transform.position = position;
        go.AddComponent<BoxCollider>();
        Component anchor = go.AddComponent(anchorType);
        SetField(anchor, "barracksId", barracksId);
        SetEnumField(anchor, "laneColor", laneColorName);
        return go;
    }

    static Type FindType(string fullName)
    {
        foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type type = assembly.GetType(fullName, throwOnError: false);
            if (type != null)
                return type;
        }

        return null;
    }

    static object Invoke(Component component, string methodName, params object[] args)
    {
        MethodInfo method = component.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public);
        Assert.That(method, Is.Not.Null, $"{component.GetType().FullName} should expose method '{methodName}'.");
        return method.Invoke(component, args);
    }

    static object InvokeNoArgs(UnityEngine.Object target, string methodName)
    {
        MethodInfo method = target.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public);
        Assert.That(method, Is.Not.Null, $"{target.GetType().FullName} should expose method '{methodName}'.");
        return method.Invoke(target, null);
    }

    static Vector3 InvokeStaticVector3(Type type, string methodName, params object[] args)
    {
        MethodInfo method = type.GetMethod(methodName, BindingFlags.Static | BindingFlags.Public);
        Assert.That(method, Is.Not.Null, $"{type.FullName} should expose static method '{methodName}'.");
        return (Vector3)method.Invoke(null, args);
    }

    static void SetField(object target, string fieldName, object value)
    {
        FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.That(field, Is.Not.Null, $"{target.GetType().FullName} should define field '{fieldName}'.");
        field.SetValue(target, value);
    }

    static void SetEnumField(object target, string fieldName, string enumName)
    {
        FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.That(field, Is.Not.Null, $"{target.GetType().FullName} should define field '{fieldName}'.");
        Assert.That(field.FieldType.IsEnum, Is.True, $"{target.GetType().FullName}.{fieldName} should be an enum field.");
        object parsedValue = Enum.Parse(field.FieldType, enumName, ignoreCase: false);
        field.SetValue(target, parsedValue);
    }

    static void SetPrivateField(object target, string fieldName, object value)
    {
        FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(field, Is.Not.Null, $"{target.GetType().FullName} should define private field '{fieldName}'.");
        field.SetValue(target, value);
    }
}
