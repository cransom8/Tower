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
                                allegianceKey = "dungeon",
                                isWaveUnit = true,
                                spawnSourceType = "scheduled_wave",
                                stance = "ATTACK",
                                pathContractType = "wave_lane",
                                routeType = "WAVE_LANE",
                                routeStartNode = "WA",
                                routeTargetNode = "A",
                                pathId = "WA_A",
                                currentWaypointIndex = 0,
                                nextWaypoint = "A",
                                currentSegment = "WA_A",
                                segmentProgress = 0f,
                                movementState = "MOVING",
                                routeWorldX = 0f,
                                routeWorldY = 0f,
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
    public System.Collections.IEnumerator WaveUnit_Preserves_Authored_Materials_When_WaveMetadata_Leaks_LaneColor()
    {
        Type registryType = FindType("CastleDefender.Game.UnitPrefabRegistry");
        Type spawnerType = FindType("CastleDefender.Game.WaveSnapshotRuntimeSpawner");

        Assert.That(registryType, Is.Not.Null);
        Assert.That(spawnerType, Is.Not.Null);

        var root = new GameObject("WaveSnapshotRuntimeSpawnerRuntimeTests_DungeonMaterialGuard");
        ScriptableObject registry = null;
        GameObject prefab = null;

        try
        {
            CreateWaveCenterAnchor("Wave_Center_Anchor", new Vector3(0f, 0f, 0f));
            CreateFrontGateAnchor("Yellow_Gate_Front", new Vector3(12f, 0f, 12f));

            Color authoredColor = new(0.18f, 0.72f, 0.34f, 1f);
            registry = ScriptableObject.CreateInstance(registryType);
            prefab = CreateUnitPrefab(authoredColor);
            ConfigureSkinEntries(registry, prefab);
            InvokeNoArgs(registry, "Rebuild");

            var spawnerHost = new GameObject("WaveRuntime_DungeonMaterialGuard");
            spawnerHost.transform.SetParent(root.transform, false);
            var spawner = spawnerHost.AddComponent(spawnerType);
            spawnerType.GetField("Registry", BindingFlags.Instance | BindingFlags.Public).SetValue(spawner, registry);

            var snapshot = new MLSnapshot
            {
                lanes = new[]
                {
                    new MLLaneSnap
                    {
                        laneIndex = 1,
                        team = "gold",
                        slotColor = "gold",
                        slotKey = "left_b",
                        branchId = "left_branch_b",
                        units = new[]
                        {
                            new MLUnit
                            {
                                id = "wu_runtime_color_guard",
                                type = "goblin",
                                skinKey = "tt_peasant",
                                laneId = 1,
                                allegianceKey = "yellow",
                                sourceTeam = "yellow",
                                isWaveUnit = true,
                                spawnSourceType = "scheduled_wave",
                                stance = "ATTACK",
                                pathContractType = "wave_lane",
                                routeType = "WAVE_LANE",
                                routeStartNode = "WB",
                                routeTargetNode = "B",
                                pathId = "WB_B",
                                currentWaypointIndex = 0,
                                nextWaypoint = "B",
                                currentSegment = "WB_B",
                                segmentProgress = 0f,
                                movementState = "MOVING",
                                routeWorldX = 0f,
                                routeWorldY = 0f,
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

            Transform waveUnit = spawnerHost.transform.Find("SnapshotUnit_wu_runtime_color_guard_goblin_tt_peasant");
            Assert.That(waveUnit, Is.Not.Null, "Wave units should still materialize when their snapshot metadata includes a lane color.");

            Renderer bodyRenderer = waveUnit.GetComponentInChildren<Renderer>();
            Assert.That(bodyRenderer, Is.Not.Null, "Wave presenter should expose a renderer.");

            Color actualColor = ReadMaterialColor(bodyRenderer.material);
            Assert.That(actualColor.r, Is.EqualTo(authoredColor.r).Within(0.08f));
            Assert.That(actualColor.g, Is.EqualTo(authoredColor.g).Within(0.08f));
            Assert.That(actualColor.b, Is.EqualTo(authoredColor.b).Within(0.08f));
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
                                allegianceKey = "dungeon",
                                isWaveUnit = true,
                                spawnSourceType = "scheduled_wave",
                                stance = "ATTACK",
                                pathContractType = "wave_lane",
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
    public System.Collections.IEnumerator RouteUnit_Combat_Does_Not_Reproject_Through_Target_Core_CombatSpace()
    {
        Type registryType = FindType("CastleDefender.Game.UnitPrefabRegistry");
        Type spawnerType = FindType("CastleDefender.Game.WaveSnapshotRuntimeSpawner");

        Assert.That(registryType, Is.Not.Null);
        Assert.That(spawnerType, Is.Not.Null);

        var root = new GameObject("WaveSnapshotRuntimeSpawnerRuntimeTests_RouteCombatProjection");
        ScriptableObject registry = null;
        GameObject prefab = null;

        try
        {
            GameObject redTownCore = CreateTownCoreAnchor("Red_TownCore_Anchor", "Red", new Vector3(-20f, 0f, 20f));
            GameObject yellowTownCore = CreateTownCoreAnchor("Yellow_TownCore_Anchor", "Gold", new Vector3(20f, 0f, 20f));
            CreateFrontGateAnchor("Yellow_Gate_Front", new Vector3(12f, 0f, 12f));
            GameObject redCenterBarracks = CreateBarracksSiteAnchor("Red_Center_Barracks_Anchor", "Red", "center", new Vector3(-6f, 0f, 12f));

            registry = ScriptableObject.CreateInstance(registryType);
            prefab = CreateUnitPrefab();
            ConfigureSkinEntries(registry, prefab);
            InvokeNoArgs(registry, "Rebuild");

            var spawnerHost = new GameObject("RouteCombatProjectionRuntime");
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
                                id = "route_combat_projection_attacker",
                                type = "infantry_t1",
                                archetypeKey = "infantry_t1",
                                presentationKey = "human_default",
                                laneId = 1,
                                allegianceKey = "red",
                                barracksId = "center",
                                spawnSourceType = "barracks_roster",
                                isWaveUnit = false,
                                stance = "ATTACK",
                                pathContractType = "barracks_cross",
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
                                segmentProgress = 0.6f,
                                movementState = "COMBAT",
                                state = "COMBAT",
                                combatTargetId = "enemy_route_unit",
                                isAttacking = true,
                                gridX = -24.8f,
                                gridY = 23.2f,
                                routeWorldX = -24f,
                                routeWorldY = 23.2f,
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

            Transform attacker = spawnerHost.transform.Find("SnapshotUnit_route_combat_projection_attacker_tt_peasant_tt_peasant");
            Assert.That(attacker, Is.Not.Null, "Route combat presenters should still materialize when the snapshot enters COMBAT.");

            Vector3 routeCenter = Vector3.Lerp(redCenterBarracks.transform.position, redTownCore.transform.position, 0.6f);
            Vector3 routeForward = (redTownCore.transform.position - redCenterBarracks.transform.position).normalized;
            Vector3 routeLateral = Vector3.Cross(routeForward, Vector3.up).normalized;
            Vector3 expected = routeCenter + (routeLateral * 0.8f);

            Assert.That(attacker.position.x, Is.EqualTo(expected.x).Within(0.08f));
            Assert.That(attacker.position.z, Is.EqualTo(expected.z).Within(0.08f));
            Assert.That(
                Vector3.Distance(attacker.position, yellowTownCore.transform.position),
                Is.GreaterThan(20f),
                "Barracks units fighting along a route should not jump into the target lane's fortress combat projection.");
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
            DestroyIfPresent("Yellow_TownCore_Anchor");
            DestroyIfPresent("Yellow_Gate_Front");
            DestroyIfPresent("Red_Center_Barracks_Anchor");
        }
    }

    [UnityTest]
    public System.Collections.IEnumerator HoldRouteUnit_Materializes_Through_Unified_SnapshotSpawner()
    {
        Type registryType = FindType("CastleDefender.Game.UnitPrefabRegistry");
        Type spawnerType = FindType("CastleDefender.Game.WaveSnapshotRuntimeSpawner");
        Type combatantType = FindType("CastleDefender.Game.LaneSnapshotCombatant");

        Assert.That(registryType, Is.Not.Null);
        Assert.That(spawnerType, Is.Not.Null);
        Assert.That(combatantType, Is.Not.Null);

        var root = new GameObject("WaveSnapshotRuntimeSpawnerRuntimeTests_HoldRoute");
        ScriptableObject registry = null;
        GameObject prefab = null;

        try
        {
            GameObject redTownCore = CreateTownCoreAnchor("Red_TownCore_Anchor", "Red", new Vector3(-20f, 0f, 20f));
            GameObject redCenterBarracks = CreateBarracksSiteAnchor("Red_Center_Barracks_Anchor", "Red", "center", new Vector3(-6f, 0f, 12f));

            registry = ScriptableObject.CreateInstance(registryType);
            prefab = CreateUnitPrefab();
            ConfigureSkinEntries(registry, prefab);
            InvokeNoArgs(registry, "Rebuild");

            var spawnerHost = new GameObject("UnifiedSnapshotRuntime_HoldRoute");
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
                                id = "hold_runtime_1",
                                type = "tt_peasant",
                                skinKey = "tt_peasant",
                                laneId = 0,
                                allegianceKey = "red",
                                isWaveUnit = false,
                                spawnSourceType = "barracks_roster",
                                stance = "HOLD",
                                pathContractType = "guard_anchor",
                                ownerLane = 0,
                                sourceLaneIndex = 0,
                                sourceTeam = "red",
                                barracksId = "center",
                                sourceBarracksKey = "center",
                                sourceBarracksId = "center",
                                pathId = "ACTR_A",
                                routeType = "CENTER_CROSS",
                                routeStartNode = "ACTR",
                                routeTargetNode = "A",
                                currentWaypointIndex = 0,
                                nextWaypoint = "A",
                                currentSegment = "ACTR_A",
                                segmentProgress = 0.5f,
                                movementState = "MOVING",
                                state = "MOVING",
                                gridX = -24f,
                                gridY = 23f,
                                routeWorldX = -24f,
                                routeWorldY = 23f,
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

            Transform routeUnit = spawnerHost.transform.Find("SnapshotUnit_hold_runtime_1_tt_peasant_tt_peasant");
            Vector3 expected = Vector3.Lerp(redCenterBarracks.transform.position, redTownCore.transform.position, 0.5f);
            Assert.That(routeUnit, Is.Not.Null, "Hold-route units should materialize through the same snapshot-driven runtime spawner as attackers.");
            Assert.That(routeUnit.position.x, Is.EqualTo(expected.x).Within(0.01f));
            Assert.That(routeUnit.position.z, Is.EqualTo(expected.z).Within(0.01f));
            Assert.That(routeUnit.GetComponent(combatantType), Is.Not.Null, "Unified snapshot units should register the shared LaneSnapshotCombatant.");
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
            DestroyIfPresent("Red_Center_Barracks_Anchor");
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
                                allegianceKey = "red",
                                barracksId = "center",
                                spawnSourceType = "barracks_roster",
                                isWaveUnit = false,
                                stance = "ATTACK",
                                pathContractType = "barracks_cross",
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
                                allegianceKey = "red",
                                barracksId = "center",
                                spawnSourceType = "barracks_roster",
                                isWaveUnit = false,
                                stance = "ATTACK",
                                pathContractType = "barracks_cross",
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
    public System.Collections.IEnumerator CombatUnits_Face_Their_Authoritative_Target_Instead_Of_Teleport_Delta()
    {
        Type registryType = FindType("CastleDefender.Game.UnitPrefabRegistry");
        Type spawnerType = FindType("CastleDefender.Game.WaveSnapshotRuntimeSpawner");

        Assert.That(registryType, Is.Not.Null);
        Assert.That(spawnerType, Is.Not.Null);

        var root = new GameObject("WaveSnapshotRuntimeSpawnerRuntimeTests_CombatFacing");
        ScriptableObject registry = null;
        GameObject prefab = null;

        try
        {
            CreateTownCoreAnchor("Red_TownCore_Anchor", "Red", new Vector3(-20f, 0f, 20f));
            CreateBarracksSiteAnchor("Red_Center_Barracks_Anchor", "Red", "center", new Vector3(-6f, 0f, 12f));

            registry = ScriptableObject.CreateInstance(registryType);
            prefab = CreateUnitPrefab();
            ConfigureSkinEntries(registry, prefab);
            InvokeNoArgs(registry, "Rebuild");

            var spawnerHost = new GameObject("CombatFacingRuntime");
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
                        units = new[]
                        {
                            new MLUnit
                            {
                                id = "combat_facing_attacker",
                                type = "infantry_t1",
                                archetypeKey = "infantry_t1",
                                presentationKey = "human_default",
                                laneId = 0,
                                allegianceKey = "red",
                                isWaveUnit = false,
                                spawnSourceType = "barracks_roster",
                                stance = "ATTACK",
                                pathContractType = "guard_anchor",
                                ownerLane = 0,
                                sourceLaneIndex = 0,
                                sourceTeam = "red",
                                barracksId = "center",
                                sourceBarracksKey = "center",
                                sourceBarracksId = "center",
                                routeType = "CENTER_CROSS",
                                routeStartNode = "ACTR",
                                routeTargetNode = "A",
                                pathId = "ACTR_A",
                                currentWaypointIndex = 0,
                                nextWaypoint = "A",
                                currentSegment = "ACTR_A",
                                segmentProgress = 0.55f,
                                movementState = "COMBAT",
                                state = "COMBAT",
                                gridX = -24.6f,
                                gridY = 23.1f,
                                routeWorldX = -24f,
                                routeWorldY = 23.1f,
                                combatTargetId = "combat_facing_target",
                                isAttacking = true,
                                hp = 10f,
                                maxHp = 10f,
                                moveSpeed = 1f,
                            },
                            new MLUnit
                            {
                                id = "combat_facing_target",
                                type = "infantry_t1",
                                archetypeKey = "infantry_t1",
                                presentationKey = "human_default",
                                laneId = 0,
                                allegianceKey = "red",
                                isWaveUnit = false,
                                spawnSourceType = "barracks_roster",
                                stance = "HOLD",
                                pathContractType = "guard_anchor",
                                ownerLane = 0,
                                sourceLaneIndex = 0,
                                sourceTeam = "red",
                                barracksId = "center",
                                sourceBarracksKey = "center",
                                sourceBarracksId = "center",
                                routeType = "CENTER_CROSS",
                                routeStartNode = "ACTR",
                                routeTargetNode = "A",
                                pathId = "ACTR_A",
                                currentWaypointIndex = 0,
                                nextWaypoint = "A",
                                currentSegment = "ACTR_A",
                                segmentProgress = 0.8f,
                                movementState = "COMBAT",
                                state = "COMBAT",
                                gridX = -23.5f,
                                gridY = 23.8f,
                                routeWorldX = -24f,
                                routeWorldY = 23.6f,
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

            Transform attacker = spawnerHost.transform.Find("SnapshotUnit_combat_facing_attacker_tt_peasant_tt_peasant");
            Transform target = spawnerHost.transform.Find("SnapshotUnit_combat_facing_target_tt_peasant_tt_peasant");
            Assert.That(attacker, Is.Not.Null);
            Assert.That(target, Is.Not.Null);

            Vector3 desiredFacing = target.position - attacker.position;
            desiredFacing.y = 0f;
            Assert.That(desiredFacing.sqrMagnitude, Is.GreaterThan(0.01f));

            Vector3 actualFacing = attacker.forward;
            actualFacing.y = 0f;
            float facingDot = Vector3.Dot(actualFacing.normalized, desiredFacing.normalized);
            Assert.That(
                facingDot,
                Is.GreaterThan(0.97f),
                "Combat presenters should face their real combat target instead of inheriting a random teleport delta.");
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
                                allegianceKey = "red",
                                barracksId = "right",
                                spawnSourceType = "barracks_roster",
                                isWaveUnit = false,
                                stance = "ATTACK",
                                pathContractType = "barracks_loop",
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
        return CreateUnitPrefab(new Color(0.72f, 0.72f, 0.72f, 1f));
    }

    static GameObject CreateUnitPrefab(Color bodyColor)
    {
        var prefab = new GameObject("ValidationWaveUnitPrefab");
        var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        body.name = "Body";
        body.transform.SetParent(prefab.transform, false);

        Renderer renderer = body.GetComponent<Renderer>();
        if (renderer != null && renderer.sharedMaterial != null)
            SetMaterialColor(renderer.material, bodyColor);

        Collider collider = body.GetComponent<Collider>();
        if (collider != null)
            UnityEngine.Object.DestroyImmediate(collider);

        return prefab;
    }

    static void SetMaterialColor(Material material, Color color)
    {
        if (material == null)
            return;

        if (material.HasProperty("_BaseColor"))
            material.SetColor("_BaseColor", color);
        if (material.HasProperty("_Color"))
            material.SetColor("_Color", color);
    }

    static Color ReadMaterialColor(Material material)
    {
        if (material == null)
            return Color.clear;

        if (material.HasProperty("_BaseColor"))
            return material.GetColor("_BaseColor");
        if (material.HasProperty("_Color"))
            return material.GetColor("_Color");
        return Color.clear;
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
