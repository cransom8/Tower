// SnapshotApplier.cs — receives game snapshots and stores the latest world state.
// Other scripts (GameplayPresentationRoot, InfoBar, TileGrid, etc.) read from SnapshotApplier.Instance.
//
// SETUP:
//   Attach to any persistent GameObject in the ML/Classic game scenes.
//   (Recommend attaching to the NetworkManager GO so it persists across scenes.)

using System;
using UnityEngine;
using CastleDefender.Net;

namespace CastleDefender.Net
{
    public class SnapshotApplier : MonoBehaviour
    {
        // ── Singleton ─────────────────────────────────────────────────────────
        public static SnapshotApplier Instance { get; private set; }

        // ── Current world state ───────────────────────────────────────────────
        public MLSnapshot      LatestML      { get; private set; }
        public ClassicSnapshot LatestClassic { get; private set; }
        public MLMatchReadyPayload LatestMLMatchReady { get; private set; }
        public MLMatchConfig       LatestMLMatchConfig { get; private set; }

        // My lane and viewing lane (ML mode)
        public int MyLaneIndex  { get; set; } = 0;
        public int ViewingLane  { get; set; } = -1;
        public int TotalLanes   { get; set; } = 1;
        float _nextDebugLogAt;
        string _lastBarracksTraceSignature;
        NetworkManager _boundNetworkManager;

        // ── Events ────────────────────────────────────────────────────────────
        public event Action<MLSnapshot>      OnMLSnapshotApplied;
        public event Action<ClassicSnapshot> OnClassicSnapshotApplied;

        // ─────────────────────────────────────────────────────────────────────
        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        void OnEnable()
        {
            TryBindNetworkManager();
        }

        void Update()
        {
            if (_boundNetworkManager != NetworkManager.Instance)
                TryBindNetworkManager();
        }

        void OnDisable()
        {
            UnbindNetworkManager();
        }

        // ─────────────────────────────────────────────────────────────────────
        void HandleMLMatchReady(MLMatchReadyPayload p)
        {
            // laneIndex is stored on NetworkManager from ml_room_created/ml_room_joined
            SyncLaneStateFromNetworkManager(NetworkManager.Instance);
            TotalLanes  = p.playerCount;
            LatestMLMatchReady = p;
            LatestML    = null;
        }

        void HandleMLMatchConfig(MLMatchConfig config)
        {
            if (config == null)
                return;

            if (LatestMLMatchConfig == null)
            {
                LatestMLMatchConfig = config;
                return;
            }

            MergeMLMatchConfig(LatestMLMatchConfig, config);
        }

        static void MergeMLMatchConfig(MLMatchConfig target, MLMatchConfig incoming)
        {
            if (target == null || incoming == null)
                return;

            if (incoming.tickHz > 0) target.tickHz = incoming.tickHz;
            if (incoming.incomeIntervalTicks > 0) target.incomeIntervalTicks = incoming.incomeIntervalTicks;
            if (incoming.startGold > 0f) target.startGold = incoming.startGold;
            if (incoming.startIncome > 0f) target.startIncome = incoming.startIncome;
            if (incoming.livesStart > 0) target.livesStart = incoming.livesStart;
            if (incoming.teamHpStart > 0) target.teamHpStart = incoming.teamHpStart;
            if (incoming.buildPhaseTicks > 0) target.buildPhaseTicks = incoming.buildPhaseTicks;
            if (incoming.transitionPhaseTicks > 0) target.transitionPhaseTicks = incoming.transitionPhaseTicks;
            if (incoming.gridW > 0) target.gridW = incoming.gridW;
            if (incoming.gridH > 0) target.gridH = incoming.gridH;
            if (!string.IsNullOrWhiteSpace(incoming.raceId)) target.raceId = incoming.raceId;
            if (incoming.loadout != null && incoming.loadout.Length > 0) target.loadout = incoming.loadout;
            if (!string.IsNullOrWhiteSpace(incoming.reconnectToken)) target.reconnectToken = incoming.reconnectToken;
            if (incoming.ranked) target.ranked = true;
            if (incoming.battlefieldTopology != null) target.battlefieldTopology = incoming.battlefieldTopology;
            if (incoming.slotDefinitions != null && incoming.slotDefinitions.Length > 0) target.slotDefinitions = incoming.slotDefinitions;
            if (incoming.fortressBuildingConfigs != null && incoming.fortressBuildingConfigs.Length > 0) target.fortressBuildingConfigs = incoming.fortressBuildingConfigs;
            if (incoming.fortressPadConfigs != null && incoming.fortressPadConfigs.Length > 0) target.fortressPadConfigs = incoming.fortressPadConfigs;
            if (incoming.barracksSiteConfigs != null && incoming.barracksSiteConfigs.Length > 0) target.barracksSiteConfigs = incoming.barracksSiteConfigs;
            if (incoming.barracksRosterConfigs != null && incoming.barracksRosterConfigs.Length > 0) target.barracksRosterConfigs = incoming.barracksRosterConfigs;
            if (incoming.heroRosterConfigs != null && incoming.heroRosterConfigs.Length > 0) target.heroRosterConfigs = incoming.heroRosterConfigs;
            if (incoming.marketRosterConfigs != null && incoming.marketRosterConfigs.Length > 0) target.marketRosterConfigs = incoming.marketRosterConfigs;
            if (incoming.barracksRosterRefundPct > 0) target.barracksRosterRefundPct = incoming.barracksRosterRefundPct;
            if (incoming.barracksSendTimerTicks > 0) target.barracksSendTimerTicks = incoming.barracksSendTimerTicks;
            if (incoming.waveTimerTicks > 0) target.waveTimerTicks = incoming.waveTimerTicks;
            if (incoming.movementTuning != null) target.movementTuning = incoming.movementTuning;
        }

        void HandleMLSnapshot(MLSnapshot snap)
        {
            SyncLaneStateFromNetworkManager(NetworkManager.Instance);
            LatestML = snap;
            if (GetLane(MyLaneIndex)?.eliminated == true)
            {
                var firstActive = FindFirstActiveLane();
                if (firstActive >= 0 && (ViewingLane < 0 || GetLane(ViewingLane)?.eliminated == true))
                    ViewingLane = firstActive;
            }
            if (Time.unscaledTime >= _nextDebugLogAt)
            {
                _nextDebugLogAt = Time.unscaledTime + 1.5f;
                var myLane = GetLane(MyLaneIndex);
                Debug.Log(
                    $"[SnapshotApplier] nmLane={NetworkManager.Instance?.MyLaneIndex ?? -1} " +
                    $"saLane={MyLaneIndex} resolved={(myLane != null ? myLane.laneIndex : -1)} " +
                    $"gold={(myLane != null ? myLane.gold : -999f)} income={(myLane != null ? myLane.income : -999f)} " +
                    $"lanes={(snap?.lanes != null ? snap.lanes.Length : 0)}"
                );
            }
            LogBarracksSnapshotTrace();
            OnMLSnapshotApplied?.Invoke(snap);
        }

#if UNITY_EDITOR
        public void DebugApplyMLSnapshot(MLSnapshot snap, int myLaneIndex = 0, int viewingLane = -1, int totalLanes = 4)
        {
            MyLaneIndex = Mathf.Clamp(myLaneIndex, 0, Mathf.Max(0, totalLanes - 1));
            ViewingLane = viewingLane >= 0 ? Mathf.Clamp(viewingLane, 0, Mathf.Max(0, totalLanes - 1)) : MyLaneIndex;
            TotalLanes = Mathf.Max(1, totalLanes);
            LatestML = snap;
            OnMLSnapshotApplied?.Invoke(snap);
        }
#endif

        void SyncLaneStateFromNetworkManager(NetworkManager nm)
        {
            if (nm == null) return;
            MyLaneIndex = nm.MyLaneIndex;
            if (ViewingLane < 0)
                ViewingLane = MyLaneIndex;
        }

        void TryBindNetworkManager()
        {
            var nm = NetworkManager.Instance;
            if (_boundNetworkManager == nm)
            {
                SyncCachedStateFromNetworkManager(nm);
                return;
            }

            UnbindNetworkManager();
            if (nm == null)
                return;

            SyncLaneStateFromNetworkManager(nm);
            nm.OnMLMatchReady         += HandleMLMatchReady;
            nm.OnMLMatchConfig        += HandleMLMatchConfig;
            nm.OnMLStateSnapshot      += HandleMLSnapshot;
            nm.OnMLSpectatorJoin      += HandleMLSpectatorJoin;
            nm.OnClassicMatchReady    += HandleClassicMatchReady;
            nm.OnClassicStateSnapshot += HandleClassicSnapshot;
            _boundNetworkManager = nm;

            SyncCachedStateFromNetworkManager(nm);
            Debug.Log($"[SnapshotApplier] Bound to NetworkManager in scene '{gameObject.scene.name}'.");
        }

        void UnbindNetworkManager()
        {
            if (_boundNetworkManager == null)
                return;

            _boundNetworkManager.OnMLMatchReady         -= HandleMLMatchReady;
            _boundNetworkManager.OnMLMatchConfig        -= HandleMLMatchConfig;
            _boundNetworkManager.OnMLStateSnapshot      -= HandleMLSnapshot;
            _boundNetworkManager.OnMLSpectatorJoin      -= HandleMLSpectatorJoin;
            _boundNetworkManager.OnClassicMatchReady    -= HandleClassicMatchReady;
            _boundNetworkManager.OnClassicStateSnapshot -= HandleClassicSnapshot;
            _boundNetworkManager = null;
        }

        void SyncCachedStateFromNetworkManager(NetworkManager nm)
        {
            if (nm == null)
                return;

            SyncLaneStateFromNetworkManager(nm);

            if (LatestMLMatchReady == null && nm.LastMLMatchReady != null)
            {
                LatestMLMatchReady = nm.LastMLMatchReady;
                TotalLanes = Mathf.Max(1, nm.LastMLMatchReady.playerCount);
            }

            if (nm.LastMatchLoadout != null && nm.LastMatchLoadout.Length > 0)
            {
                var cachedConfig = new MLMatchConfig { loadout = nm.LastMatchLoadout };
                if (LatestMLMatchConfig == null)
                    LatestMLMatchConfig = cachedConfig;
                else
                    MergeMLMatchConfig(LatestMLMatchConfig, cachedConfig);
            }
        }

        void HandleMLSpectatorJoin(MLSpectatorJoinPayload _)
        {
            var firstActive = FindFirstActiveLane();
            ViewingLane = firstActive >= 0 ? firstActive : MyLaneIndex;
        }

        void HandleClassicMatchReady(ClassicMatchReadyPayload p)
        {
            LatestClassic = null;
        }

        void HandleClassicSnapshot(ClassicSnapshot snap)
        {
            LatestClassic = snap;
            OnClassicSnapshotApplied?.Invoke(snap);
        }

        // ─────────────────────────────────────────────────────────────────────
        /// <summary>Returns the lane snap for a given index, or null.</summary>
        public MLLaneSnap GetLane(int index)
        {
            if (LatestML?.lanes == null) return null;
            for (int i = 0; i < LatestML.lanes.Length; i++)
            {
                var lane = LatestML.lanes[i];
                if (lane != null && lane.laneIndex == index) return lane;
            }
            return null;
        }

        public MLLaneSnap MyLane     => GetLane(MyLaneIndex);
        public MLLaneSnap ViewedLane => GetLane(ViewingLane);
        public MLBattlefieldTopology CurrentBattlefieldTopology
            => LatestML?.battlefieldTopology
            ?? LatestMLMatchConfig?.battlefieldTopology
            ?? LatestMLMatchReady?.battlefieldTopology;

        public MLFortressPad GetFortressPad(int laneIndex, string padId)
        {
            if (string.IsNullOrWhiteSpace(padId)) return null;
            var lane = GetLane(laneIndex);
            var pads = lane?.fortressPads;
            if (pads == null) return null;
            for (int i = 0; i < pads.Length; i++)
            {
                var pad = pads[i];
                if (pad != null && string.Equals(pad.padId, padId, StringComparison.OrdinalIgnoreCase))
                    return pad;
            }
            return null;
        }

        public MLFortressPad GetFortressPadByBuildingType(int laneIndex, string buildingType)
        {
            if (string.IsNullOrWhiteSpace(buildingType)) return null;
            var lane = GetLane(laneIndex);
            var pads = lane?.fortressPads;
            if (pads == null) return null;
            for (int i = 0; i < pads.Length; i++)
            {
                var pad = pads[i];
                if (pad != null && string.Equals(pad.buildingType, buildingType, StringComparison.OrdinalIgnoreCase))
                    return pad;
            }
            return null;
        }

        public MLFortressPad GetTownCorePad(int laneIndex)
            => GetFortressPadByBuildingType(laneIndex, "town_core");

        public bool TryGetTownCoreHp(int laneIndex, out int currentHp, out int maxHp)
        {
            currentHp = 0;
            maxHp = 0;

            var pad = GetTownCorePad(laneIndex);
            if (pad == null)
                return false;

            currentHp = Mathf.Max(0, Mathf.RoundToInt(pad.hp));
            maxHp = Mathf.Max(0, Mathf.RoundToInt(pad.maxHp));
            return true;
        }

        public int GetTickHz()
        {
            int tickHz = LatestMLMatchConfig != null ? LatestMLMatchConfig.tickHz : 0;
            return tickHz > 0 ? tickHz : 20;
        }

        public int GetWaveTimerTicksRemaining()
        {
            return Mathf.Max(0, LatestML != null ? LatestML.waveTimerTicksRemaining : 0);
        }

        public int GetWaveTimerTotalTicks()
        {
            int total = LatestML != null ? LatestML.waveTimerTotalTicks : 0;
            if (total <= 0 && LatestMLMatchConfig != null)
                total = LatestMLMatchConfig.waveTimerTicks;
            return Mathf.Max(0, total);
        }

        public int GetWaveTimerSecondsRemaining()
        {
            int tickHz = GetTickHz();
            return Mathf.CeilToInt(GetWaveTimerTicksRemaining() / Mathf.Max(1f, tickHz));
        }

        public int GetBarracksSendSecondsRemaining(int laneIndex)
        {
            int tickHz = GetTickHz();
            var lane = GetLane(laneIndex);
            int ticksRemaining = lane != null ? Mathf.Max(0, lane.barracksSendTimerTicksRemaining) : 0;
            return Mathf.CeilToInt(ticksRemaining / Mathf.Max(1f, tickHz));
        }

        public int GetBarracksSiteSendSecondsRemaining(int laneIndex, string barracksId)
        {
            int tickHz = GetTickHz();
            var site = GetBarracksSite(laneIndex, barracksId);
            int ticksRemaining = site != null ? Mathf.Max(0, site.sendTimerTicksRemaining) : 0;
            return Mathf.CeilToInt(ticksRemaining / Mathf.Max(1f, tickHz));
        }

        public MLBarracksSite GetBarracksSite(int laneIndex, string barracksId)
        {
            if (string.IsNullOrWhiteSpace(barracksId)) return null;
            var lane = GetLane(laneIndex);
            var sites = lane?.barracksSites;
            if (sites == null) return null;
            for (int i = 0; i < sites.Length; i++)
            {
                var site = sites[i];
                if (site != null && string.Equals(site.barracksId, barracksId, StringComparison.OrdinalIgnoreCase))
                    return site;
            }
            return null;
        }

        public MLBarracksRosterEntry GetBarracksRosterEntry(int laneIndex, string rosterKey)
        {
            if (string.IsNullOrWhiteSpace(rosterKey)) return null;
            var lane = GetLane(laneIndex);
            var roster = lane?.barracksRoster;
            if (roster == null) return null;
            for (int i = 0; i < roster.Length; i++)
            {
                var entry = roster[i];
                if (entry != null && string.Equals(entry.rosterKey, rosterKey, StringComparison.OrdinalIgnoreCase))
                    return entry;
            }
            return null;
        }

        public MLHeroRosterEntry GetHeroRosterEntry(int laneIndex, string heroKey)
        {
            if (string.IsNullOrWhiteSpace(heroKey)) return null;
            var lane = GetLane(laneIndex);
            var roster = lane?.heroRoster;
            if (roster == null) return null;
            for (int i = 0; i < roster.Length; i++)
            {
                var entry = roster[i];
                if (entry != null && string.Equals(entry.heroKey, heroKey, StringComparison.OrdinalIgnoreCase))
                    return entry;
            }
            return null;
        }

        public MLBarracksRosterEntry GetBarracksSiteRosterEntry(int laneIndex, string barracksId, string rosterKey)
        {
            if (string.IsNullOrWhiteSpace(rosterKey)) return null;
            var site = GetBarracksSite(laneIndex, barracksId);
            var roster = site?.roster;
            if (roster == null) return null;
            for (int i = 0; i < roster.Length; i++)
            {
                var entry = roster[i];
                if (entry != null && string.Equals(entry.rosterKey, rosterKey, StringComparison.OrdinalIgnoreCase))
                    return entry;
            }
            return null;
        }

        public MLFortressPadConfig GetFortressPadConfig(string padId)
        {
            if (string.IsNullOrWhiteSpace(padId)) return null;
            var pads = LatestMLMatchConfig?.fortressPadConfigs;
            if (pads == null) return null;
            for (int i = 0; i < pads.Length; i++)
            {
                var pad = pads[i];
                if (pad != null && string.Equals(pad.padId, padId, StringComparison.OrdinalIgnoreCase))
                    return pad;
            }
            return null;
        }

        public MLBarracksRosterConfig GetBarracksRosterConfig(string rosterKey)
        {
            if (string.IsNullOrWhiteSpace(rosterKey)) return null;
            var roster = LatestMLMatchConfig?.barracksRosterConfigs;
            if (roster == null) return null;
            for (int i = 0; i < roster.Length; i++)
            {
                var entry = roster[i];
                if (entry != null && string.Equals(entry.rosterKey, rosterKey, StringComparison.OrdinalIgnoreCase))
                    return entry;
            }
            return null;
        }

        public MLHeroRosterConfig GetHeroRosterConfig(string heroKey)
        {
            if (string.IsNullOrWhiteSpace(heroKey)) return null;
            var roster = LatestMLMatchConfig?.heroRosterConfigs;
            if (roster == null) return null;
            for (int i = 0; i < roster.Length; i++)
            {
                var entry = roster[i];
                if (entry != null && string.Equals(entry.heroKey, heroKey, StringComparison.OrdinalIgnoreCase))
                    return entry;
            }
            return null;
        }

        public MLBarracksSiteConfig GetBarracksSiteConfig(string barracksId)
        {
            if (string.IsNullOrWhiteSpace(barracksId)) return null;
            var sites = LatestMLMatchConfig?.barracksSiteConfigs;
            if (sites == null) return null;
            for (int i = 0; i < sites.Length; i++)
            {
                var entry = sites[i];
                if (entry != null && string.Equals(entry.barracksId, barracksId, StringComparison.OrdinalIgnoreCase))
                    return entry;
            }
            return null;
        }

        public MLLaneAssignment GetLaneAssignment(int index)
        {
            var assignments = LatestMLMatchReady?.laneAssignments;
            if (assignments == null) return null;
            for (int i = 0; i < assignments.Length; i++)
            {
                var a = assignments[i];
                if (a != null && a.laneIndex == index) return a;
            }
            return null;
        }

        int FindFirstActiveLane()
        {
            if (LatestML?.lanes == null) return -1;
            foreach (var lane in LatestML.lanes)
            {
                if (lane != null && !lane.eliminated)
                    return lane.laneIndex;
            }
            return -1;
        }

        void LogBarracksSnapshotTrace()
        {
            var lane = MyLane;
            if (lane == null)
                return;

            string signature = BuildBarracksTraceSignature(lane);
            if (string.IsNullOrWhiteSpace(signature) || string.Equals(signature, _lastBarracksTraceSignature, StringComparison.Ordinal))
                return;

            _lastBarracksTraceSignature = signature;
            Debug.Log(
                $"[BarracksTrace][ClientSnapshot] lane={lane.laneIndex} slotColor='{lane.slotColor}' " +
                $"sendTicks={lane.barracksSendTimerTicksRemaining}/{lane.barracksSendTimerTotalTicks} {signature}");
        }

        static string BuildBarracksTraceSignature(MLLaneSnap lane)
        {
            if (lane == null || lane.barracksSites == null || lane.barracksSites.Length == 0)
                return string.Empty;

            string summary = string.Empty;
            for (int i = 0; i < lane.barracksSites.Length; i++)
            {
                var site = lane.barracksSites[i];
                if (site == null)
                    continue;

                if (summary.Length > 0)
                    summary += " ";

                summary += $"{site.barracksId}[built={site.isBuilt}";
                var roster = site.roster;
                bool hasOwned = false;
                if (roster != null)
                {
                    for (int rosterIndex = 0; rosterIndex < roster.Length; rosterIndex++)
                    {
                        var entry = roster[rosterIndex];
                        if (entry == null || entry.ownedCount <= 0)
                            continue;

                        summary += hasOwned ? "," : " roster=";
                        summary += $"{entry.rosterKey}:{entry.ownedCount}";
                        hasOwned = true;
                    }
                }

                if (!hasOwned)
                    summary += " roster=<empty>";

                summary += "]";
            }

            return summary;
        }

        public bool AreLanesAllied(int laneA, int laneB)
        {
            var a = GetLane(laneA);
            var b = GetLane(laneB);
            if (a != null && b != null && !string.IsNullOrEmpty(a.team) && !string.IsNullOrEmpty(b.team))
                return string.Equals(a.team, b.team, StringComparison.OrdinalIgnoreCase);

            var aa = GetLaneAssignment(laneA);
            var bb = GetLaneAssignment(laneB);
            if (aa != null && bb != null && !string.IsNullOrEmpty(aa.team) && !string.IsNullOrEmpty(bb.team))
                return string.Equals(aa.team, bb.team, StringComparison.OrdinalIgnoreCase);

            return laneA == laneB;
        }

        public Color GetLaneColor(int laneIndex, Color fallback)
        {
            var lane = GetLane(laneIndex);
            if (lane != null && TryResolveSlotColor(lane.slotColor, out var laneColor))
                return laneColor;

            var assignment = GetLaneAssignment(laneIndex);
            if (assignment != null && TryResolveSlotColor(assignment.slotColor, out var assignmentColor))
                return assignmentColor;

            return fallback;
        }

        public static bool TryResolveSlotColor(string slotColor, out Color color)
        {
            switch ((slotColor ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "red":
                    color = new Color(0.86f, 0.25f, 0.22f);
                    return true;
                case "gold":
                case "yellow":
                    color = new Color(0.92f, 0.74f, 0.20f);
                    return true;
                case "blue":
                    color = new Color(0.24f, 0.50f, 0.92f);
                    return true;
                case "green":
                    color = new Color(0.20f, 0.72f, 0.42f);
                    return true;
                default:
                    if (!string.IsNullOrWhiteSpace(slotColor) && ColorUtility.TryParseHtmlString(slotColor, out color))
                        return true;
                    color = Color.white;
                    return false;
            }
        }
    }
}
