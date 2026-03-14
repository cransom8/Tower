// LaneRenderer.cs — Spawns, moves, and removes unit GameObjects for the full ML battlefield.
// Renders units from ALL lanes simultaneously, positioned in their branch world space.
// Uses normProgress-based dead-reckoning: velocity is estimated from consecutive snapshots
// and units are extrapolated forward each frame so movement is continuous at any framerate,
// not quantized to 10 Hz snapshot steps.
// Separate from TileGrid.cs which handles the viewed branch's tile structures.
//
// SETUP (Game_ML.unity):
//   Attach to any GameObject (e.g. "LaneRenderer" GO).
//   Inspector:
//     Registry       — UnitPrefabRegistry ScriptableObject with key→prefab mappings.
//     HpBarPrefab    — optional world-space Image prefab for HP bars above units.

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using CastleDefender.Net;

namespace CastleDefender.Game
{
    public class LaneRenderer : MonoBehaviour
    {
        // ── Inspector ─────────────────────────────────────────────────────────
        [Header("Unit prefab registry (key → prefab)")]
        public UnitPrefabRegistry Registry;

        [Header("HP bar prefab (optional WorldSpace Canvas Image)")]
        public GameObject HpBarPrefab;

        // ── Runtime state ─────────────────────────────────────────────────────
        class UnitView
        {
            public GameObject go;
            public Transform  hpBarFill;
            public Transform  hpBarFillAnchor;
            public Image      hpBarImage;
            public Vector3    hpBarFillBaseScale = Vector3.one;
            public Vector3    hpBarFillBaseLocalPosition = Vector3.zero;
            public string     typeKey;
            public bool       isMine;
            public int        ownerLane;
            public int        level;

            // Dead-reckoning: last known world position + estimated velocity
            public Vector3 worldPos;        // world position at last snapshot
            public Vector3 worldVelocity;   // world units/sec, estimated between snapshots
            public float   timeSinceSnap;   // seconds since last snapshot update
            public bool    hadSnapshot;     // true after the second snapshot (velocity valid)

            // Kept for backward compat / shared-suffix units
            public float normProgress;

            // Damage flash
            public float      lastHp       = -1f;
            public float      flashTimer   =  0f;
            public Color      baseColor    = Color.white;
            public Renderer[] renderers;          // cached at spawn (P3)

            // Animation
            public Animator anim;
            public bool     wasMoving;
            public bool     wasAttacking;
            public int      nextAttackVariant;
        }

        readonly Dictionary<string, UnitView> _units    = new();
        readonly HashSet<string>              _seenIds  = new();
        readonly List<string>                 _toRemove = new();
        float  _lastSnapTime = -1f;
        bool   _subscribed;
        readonly int[] _branchMap = new int[4] { 0, 1, 2, 3 }; // laneIndex → branchCfg
        string _roundState   = "build";   // "build" | "combat" | "transition"
        static readonly Vector3 HpBarScaleBoost = new(1.85f, 1.55f, 1.40f);
        static readonly Color HpBarFrameColor   = new(0.03f, 0.03f, 0.03f, 1f);
        static readonly Color HpBarShadowColor  = new(0.11f, 0.08f, 0.08f, 1f);
        static readonly Dictionary<string, float> HpBarTypeOffset = new(System.StringComparer.OrdinalIgnoreCase)
        {
            { "goblin", 0.28f },
            { "kobold", 0.28f },
            { "runner", 0.24f },
            { "archer", 0.24f },
            { "fighter", 0.28f },
            { "mage", 0.30f },
            { "ballista", 0.44f },
            { "cannon", 0.44f },
            { "footman", 0.28f },
            { "ironclad", 0.38f },
            { "warlock", 0.36f },
            { "golem", 0.56f },
            { "ogre", 0.50f },
            { "troll", 0.42f },
            { "werewolf", 0.34f },
            { "griffin", 0.48f },
            { "chimera", 0.62f },
            { "manticora", 0.64f },
            { "mountain_dragon", 0.80f },
            { "giant_viper", 0.34f },
            { "evil_watcher", 0.42f },
            { "hydra", 0.74f },
        };

        // State name tables — covers PascalCase (Orc/Ghoul style), camelCase (Goblin/Werewolf/Cyclops),
        // and animal variants (Wolf/GiantRat/Spider). Order = priority (first match wins).
        static readonly string[] _walkStates = {
            // PascalCase generics
            "Walk", "Walk_RM", "Run", "Run_RM",
            // Goblin weapon-variant camelCase
            "walkForwardSwordShield", "walkNormalSwordShield",
            "walkForwardDaggers",     "walkNormalDaggers",
            "walkForwardSlingshot",   "walkNormalSlingshot",
            // Other camelCase generics (Werewolf, Cyclops, animals)
            "walk", "walk_RM", "run", "run_RM",
        };

        static readonly string[] _idleStates = {
            // PascalCase
            "IdleNormal", "IdleCombat", "IdleBlock", "Idle",
            "IdleLookAround", "IdleAggressive", "IdleBreathe",
            // Goblin camelCase
            "idleSwordShield", "idleDaggers", "idleSlingshot",
            // camelCase generics
            "idleLookAround", "idleBreathe", "idle",
        };

        static readonly string[] _attackStates = {
            // PascalCase (Orc, Ghoul, Vampire, etc.)
            "Attack1", "Attack1Forward", "Attack2", "Attack2Forward",
            // Animal attacks
            "Bite", "JumpBite", "RunBite", "GrabBite",
            // Goblin camelCase
            "attack1SwordShield", "attack1ForwardSwordShield",
            "attack1Daggers",     "attack1ForwardDaggers",
            "shootSlingshot",
            // camelCase generics (Werewolf, Cyclops)
            "clawsAttack", "clawsAttackLeft", "clawsAttackRight",
            "rightHandAttack", "leftHandAttack",
            // Wyvern (HEROIC FANTASY CREATURES)
            "SimpleBiteAttack", "StingerAttack", "SpecialFinishBiteAttack",
            "FlyStationarySpitFireball", "SpitFireball",
            // Misc
            "Attack", "attack",
        };

        static readonly string[] _deathStates = {
            // PascalCase
            "Death", "Die",
            // Goblin camelCase
            "deathSwordShield", "deathDaggers", "deathSlingshot",
            // camelCase generics
            "death",
        };

        // ─────────────────────────────────────────────────────────────────────
        void OnEnable()  => TrySubscribeSnapshots();

        void OnDisable()
        {
            if (_subscribed && SnapshotApplier.Instance != null)
                SnapshotApplier.Instance.OnMLSnapshotApplied -= OnSnapshot;
            _subscribed = false;
        }

        void Update()
        {
            TrySubscribeSnapshots();

            float dt = Time.deltaTime;
            foreach (var view in _units.Values)
            {
                if (view?.go == null) continue;
                view.timeSinceSnap += dt;
                if (view.hadSnapshot)
                    view.go.transform.position = view.worldPos + view.worldVelocity * view.timeSinceSnap;

                // Drive damage flash: lerp from red back to base colour over 0.25 s
                if (view.flashTimer > 0f)
                {
                    view.flashTimer -= dt;
                    float t = Mathf.Clamp01(view.flashTimer / 0.25f);
                    Color flash = Color.Lerp(view.baseColor, Color.red, t);
                    ApplyTintToRenderers(view.renderers, flash);
                }
            }
        }

        static void ApplyTintToRenderers(Renderer[] renderers, Color col)
        {
            if (renderers == null) return;
            foreach (var r in renderers)
            {
                if (r == null) continue;
                var bridge = r.GetComponent<ToonShaderBridge>();
                if (bridge != null) { bridge.SetBaseColor(col); continue; }
                foreach (var mat in r.materials)
                {
                    if (mat == null) continue;
                    if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", col);
                    else mat.color = col;
                }
            }
        }

        void TrySubscribeSnapshots()
        {
            if (_subscribed) return;
            var sa = SnapshotApplier.Instance;
            if (sa == null) return;

            sa.OnMLSnapshotApplied += OnSnapshot;
            _subscribed = true;

            if (sa.LatestML != null) OnSnapshot(sa.LatestML);
        }

        // ─────────────────────────────────────────────────────────────────────
        void OnSnapshot(MLSnapshot snap)
        {
            if (snap?.lanes == null) return;
            var sa = SnapshotApplier.Instance;

            float now      = Time.time;
            float snapDt   = _lastSnapTime >= 0f ? now - _lastSnapTime : 0.1f;
            _lastSnapTime  = now;

            // Track round state so stopped units know whether to idle or attack
            string prevRoundState = _roundState;
            _roundState = snap.roundState ?? "build";
            bool roundStateChanged = _roundState != prevRoundState;

            // Rebuild branchMap from current snapshot lane metadata (zero-alloc)
            for (int i = 0; i < _branchMap.Length; i++) _branchMap[i] = i;
            foreach (var ls in snap.lanes)
            {
                if (ls == null || (uint)ls.laneIndex >= (uint)_branchMap.Length) continue;
                int bc = TileGrid.GetBranchConfigIndex(ls.branchId);
                if (bc >= 0) _branchMap[ls.laneIndex] = bc;
            }

            _seenIds.Clear();

            foreach (var lane in snap.lanes)
            {
                if (lane?.units == null) continue;
                foreach (var u in lane.units)
                {
                    _seenIds.Add(u.id);

                    if (!_units.TryGetValue(u.id, out var view) || view.go == null)
                        view = CreateUnit(u, sa);

                    if (view?.go == null) continue;

                    // Determine world position: use actual tile coords for branch units so
                    // they visually respect wall placement. Fall back to the polyline for
                    // units on the shared suffix (beyond the private build grid).
                    // Wave units have ownerLane=-1 (no player owner); use the lane they
                    // belong to in the snapshot so they render on the correct branch.
                    int dataLane    = u.ownerLane >= 0 ? u.ownerLane : lane.laneIndex;  // server lane (data lookup)
                    int spatialLane = (uint)dataLane < (uint)_branchMap.Length ? _branchMap[dataLane] : dataLane;

                    MLLaneSnap ownerLaneSnap = null;
                    foreach (var ls in snap.lanes)
                        if (ls != null && ls.laneIndex == dataLane) { ownerLaneSnap = ls; break; }

                    int branchLen = ownerLaneSnap?.path?.Length ?? 0;
                    bool onBranch = branchLen > 0 && u.pathIdx < branchLen;

                    Vector3 newWorldPos;
                    if (u.isDefender && !onBranch)
                    {
                        // Defender on the suffix (merge bridge): use the suffix polyline so they
                        // appear at the correct world position (pt3 = Island_Split exit) rather
                        // than extrapolating off the end of the branch tile grid.
                        newWorldPos = TileGrid.SuffixProgressToWorld(spatialLane, u.normProgress);
                    }
                    else if (u.isDefender || (u.isWaveUnit && onBranch))
                    {
                        // Defenders on the branch grid and wave units use direct 2D tile coords
                        // so the full rectangle spawn spread is rendered correctly.
                        newWorldPos = TileGrid.TileToWorld(spatialLane, u.gridX, u.gridY);
                    }
                    else if (onBranch && ownerLaneSnap.path != null && ownerLaneSnap.path.Length >= 2)
                    {
                        // Player-sent units: interpolate along the centre path using pathIdx.
                        int   p0 = Mathf.Clamp(Mathf.FloorToInt(u.pathIdx), 0, branchLen - 1);
                        int   p1 = Mathf.Clamp(p0 + 1,                       0, branchLen - 1);
                        float ft = u.pathIdx - p0;
                        var  t0  = ownerLaneSnap.path[p0];
                        var  t1  = ownerLaneSnap.path[p1];
                        newWorldPos = Vector3.Lerp(
                            TileGrid.TileToWorld(spatialLane, t0.x, t0.y),
                            TileGrid.TileToWorld(spatialLane, t1.x, t1.y),
                            ft);
                    }
                    else
                    {
                        // Suffix: normProgress is suffix-relative (0=grid end/pt2, 1=castle/pt5).
                        newWorldPos = TileGrid.SuffixProgressToWorld(spatialLane, u.normProgress);
                    }

                    // Estimate world-space velocity from position delta between snapshots.
                    if (view.hadSnapshot && snapDt > 0f)
                    {
                        view.worldVelocity = (newWorldPos - view.worldPos) / snapDt;
                        // Update facing to match travel direction
                        if (view.worldVelocity.sqrMagnitude > 0.01f && view.go != null)
                            view.go.transform.rotation = Quaternion.LookRotation(view.worldVelocity.normalized, Vector3.up);
                    }
                    else
                        view.worldVelocity = Vector3.zero;

                    view.worldPos      = newWorldPos;
                    view.normProgress  = u.normProgress;
                    view.timeSinceSnap = 0f;
                    view.ownerLane     = dataLane;
                    view.hadSnapshot   = true;

                    // Drive animation using authoritative server state:
                    //   isAttacking (combatTarget set) → attack
                    //   moving (position advancing)    → walk
                    //   otherwise                      → idle
                    if (view.anim != null)
                    {
                        bool attacking = u.isAttacking;
                        bool moving    = !attacking && view.worldVelocity.sqrMagnitude > 0.01f;

                        if (attacking != view.wasAttacking || moving != view.wasMoving || roundStateChanged)
                        {
                            if (attacking)
                                PlayAttackVariant(view);
                            else if (moving)
                                TryCrossFade(view.anim, _walkStates, 0.15f);
                            else
                                TryCrossFade(view.anim, _idleStates, 0.20f);
                            view.wasAttacking = attacking;
                            view.wasMoving    = moving;
                        }
                    }

                    if ((view.hpBarFill != null || view.hpBarImage != null) && u.maxHp > 0f)
                        UpdateHpBarVisual(view, Mathf.Clamp01(u.hp / u.maxHp));

                    // Trigger red flash when HP drops
                    if (view.lastHp >= 0f && u.hp < view.lastHp)
                        view.flashTimer = 0.25f;
                    view.lastHp = u.hp;
                }
            }

            // Remove units that are no longer in any lane's snapshot
            _toRemove.Clear();
            foreach (var kv in _units)
                if (!_seenIds.Contains(kv.Key)) _toRemove.Add(kv.Key);

            foreach (var id in _toRemove)
            {
                var v = _units[id];
                if (v?.go != null)
                {
                    AudioManager.I?.Play(AudioManager.SFX.UnitDeath, 0.4f);
                    StartCoroutine(PlayDeathThenDestroy(v.go, v.anim));
                }
                _units.Remove(id);
            }
        }

        UnitView CreateUnit(MLUnit u, SnapshotApplier sa)
        {
            int  myLane = sa != null ? sa.MyLaneIndex : 0;
            bool isMine = u.ownerLane == myLane;
            bool isAlly = sa != null && sa.AreLanesAllied(u.ownerLane, myLane);

            GameObject prefab = Registry != null ? Registry.GetPrefabForSkin(u.type, u.skinKey) : null;
            if (prefab == null)
            {
                Debug.LogWarning("[LaneRenderer] No prefab for unit type: " + u.type);
                return new UnitView();
            }

            int ownerDataLane = u.ownerLane >= 0 ? u.ownerLane : 0;
            int ownerBranchCfg = ownerDataLane;
            if (sa?.LatestML?.lanes != null)
                foreach (var ls in sa.LatestML.lanes)
                    if (ls != null && ls.laneIndex == ownerDataLane)
                    { int bc = TileGrid.GetBranchConfigIndex(ls.branchId); if (bc >= 0) ownerBranchCfg = bc; break; }

            const int branchLen = 28; // GRID_H — matches server constant
            Vector3 spawnPos = u.pathIdx < branchLen
                ? TileGrid.TileToWorld(ownerBranchCfg, (int)u.gridX, (int)u.gridY)
                : TileGrid.SuffixProgressToWorld(ownerBranchCfg, u.normProgress);
            Vector3 spawnFwd = TileGrid.GetLaneForwardDir(ownerBranchCfg);
            var go = Instantiate(prefab, spawnPos, Quaternion.LookRotation(spawnFwd, Vector3.up), transform);
            go.name = $"Unit_{u.id}_{u.type}_{u.skinKey ?? "default"}";

            int   unitLevel  = u.level > 0 ? u.level : 1;
            float baseScale  = Registry != null ? Registry.GetScaleForSkin(u.type, u.skinKey) : 1f;
            float scale      = baseScale * GetLevelScale(unitLevel);
            go.transform.localScale = Vector3.one * scale;

            // Wave enemies use a hostile red/orange tint; player-sent units use team colours.
            Color col, rim;
            if (u.isWaveUnit)
            {
                col = new Color(0.90f, 0.30f, 0.10f);  // hostile red-orange
                rim = new Color(1.00f, 0.55f, 0.00f);  // fiery orange rim
            }
            else
            {
                Color fallback = Registry != null
                    ? (isMine ? Registry.GetTintMine(u.type) : Registry.GetTintEnemy(u.type))
                    : (isMine ? new Color(0.20f, 0.80f, 0.70f) : new Color(0.90f, 0.25f, 0.25f));
                col = sa != null ? sa.GetLaneColor(u.ownerLane, fallback) : fallback;
                rim = isMine
                    ? Color.Lerp(col, Color.white, 0.35f)
                    : isAlly
                        ? Color.Lerp(col, Color.white, 0.20f)
                        : Color.Lerp(col, Color.black, 0.25f);
            }

            foreach (var r in go.GetComponentsInChildren<Renderer>())
            {
                UpgradeLegacyRendererMaterials(r);
                var bridge = r.GetComponent<ToonShaderBridge>();
                if (bridge != null) { bridge.SetBaseColor(col); bridge.SetRimColor(rim); continue; }

                var instanced = r.materials;
                for (int mi = 0; mi < instanced.Length; mi++)
                {
                    var mat = instanced[mi];
                    if (mat == null) continue;
                    if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", col);
                    else                               mat.color = col;
                }
                r.materials = instanced;
            }

            Transform hpFill = null;
            Transform hpFillAnchor = null;
            Image hpImage = null;
            if (HpBarPrefab != null)
            {
                var bar = CreateUnitHpBar(go.transform, out hpFill, out hpFillAnchor, out hpImage);
                PositionHpBarOverHead(go, bar.transform, u.type);
            }

            var unitAnim = go.GetComponentInChildren<Animator>();
            if (unitAnim != null)
            {
                // Force Animator to fully initialize before we drive any state.
                // Without this, CrossFade/Play called in the same frame as Instantiate
                // gets silently overridden by the controller's default entry state.
                unitAnim.Rebind();
                unitAnim.Update(0f);
            }

            var view = new UnitView
            {
                go             = go,
                hpBarFill      = hpFill,
                hpBarFillAnchor = hpFillAnchor,
                hpBarImage     = hpImage,
                hpBarFillBaseScale = hpFill != null ? hpFill.localScale : Vector3.one,
                hpBarFillBaseLocalPosition = hpFill != null ? hpFill.localPosition : Vector3.zero,
                anim           = unitAnim,
                wasMoving      = false,
                typeKey        = u.type,
                isMine         = isMine,
                ownerLane      = u.ownerLane,
                level          = unitLevel,
                normProgress   = u.normProgress,
                worldPos       = spawnPos,
                worldVelocity  = Vector3.zero,
                timeSinceSnap  = 0f,
                hadSnapshot    = false,
                baseColor      = col,
                renderers      = go.GetComponentsInChildren<Renderer>(),
            };
            if (hpFill != null || hpImage != null) UpdateHpBarVisual(view, 1f);
            if (unitAnim != null) SetAnimIdle(view);   // idle until first snapshot velocity known
            _units[u.id] = view;

            AudioManager.I?.Play(AudioManager.SFX.UnitSpawn, isMine ? 0.6f : 0.3f);
            return view;
        }

        // ── Level helpers ─────────────────────────────────────────────────────

        // Returns a scale multiplier based on barracks level.
        // Level 1 = 1.65× registry base (registry scales already tuned to small baseline).
        // Each level adds ~10 % so level 4 ≈ 2.0×, giving clear visible growth.
        static float GetLevelScale(int level)
        {
            int lvl = Mathf.Clamp(level, 1, 10);
            return 1.55f + lvl * 0.10f;  // 1→1.65, 2→1.75, 3→1.85, 4→1.95 …
        }

        // Adds N-1 thin notch dividers to the bar for level N (level 1 = no notches).
        // Notches visually segment the HP bar so unit level is readable at a glance.
        static void AddHpBarNotches(Transform barRoot, int level)
        {
            if (level <= 1) return;

            // Background cube runs localX −0.5 … +0.5 (scale.x = 1).
            // Place one divider per internal boundary (level−1 dividers for level segments).
            for (int i = 1; i < level; i++)
            {
                float xPos = (float)i / level - 0.5f;

                var notch = GameObject.CreatePrimitive(PrimitiveType.Cube);
                notch.name = "Notch";
                notch.transform.SetParent(barRoot, false);
                notch.transform.localPosition = new Vector3(xPos, 0f, -0.009f);
                notch.transform.localScale    = new Vector3(0.018f, 0.14f, 0.07f);

                // Remove collider — purely visual.
                var col = notch.GetComponent<Collider>();
                if (col != null) Object.Destroy(col);

                var rend = notch.GetComponent<Renderer>();
                if (rend != null)
                {
                    // Use a simple instanced material so each notch is independent.
                    rend.material.color = new Color(0.04f, 0.04f, 0.04f, 1f);
                }
            }
        }

        // ── Animation helpers ─────────────────────────────────────────────────
        static GameObject CreateUnitHpBar(Transform parent, out Transform fillTransform, out Transform fillAnchor, out Image fillImage)
        {
            var root = new GameObject("HpBarUI", typeof(RectTransform), typeof(Canvas));
            root.transform.SetParent(parent, false);
            root.transform.localScale = Vector3.one * 0.01f;

            var canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = 200;

            var rootRt = root.GetComponent<RectTransform>();
            rootRt.sizeDelta = new Vector2(72f, 12f);

            if (root.GetComponent<CastleDefender.FX.BillboardY>() == null)
                root.AddComponent<CastleDefender.FX.BillboardY>();

            CreateBarOutline(root.transform);

            var anchorGO = new GameObject("FillAnchor", typeof(RectTransform));
            anchorGO.transform.SetParent(root.transform, false);
            var anchorRt = anchorGO.GetComponent<RectTransform>();
            anchorRt.anchorMin = new Vector2(0.06f, 0.22f);
            anchorRt.anchorMax = new Vector2(0.94f, 0.78f);
            anchorRt.offsetMin = Vector2.zero;
            anchorRt.offsetMax = Vector2.zero;
            anchorRt.pivot = new Vector2(0f, 0.5f);

            fillImage = CreateBarImage("Fill", anchorGO.transform, new Color(0.18f, 0.94f, 0.34f, 0.96f));
            var fillRt = fillImage.rectTransform;
            fillRt.anchorMin = new Vector2(0f, 0f);
            fillRt.anchorMax = new Vector2(1f, 1f);
            fillRt.offsetMin = Vector2.zero;
            fillRt.offsetMax = Vector2.zero;
            fillRt.pivot = new Vector2(0f, 0.5f);

            fillTransform = fillRt;
            fillAnchor = null;

            return root;
        }

        static Image CreateBarImage(string name, Transform parent, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var image = go.GetComponent<Image>();
            image.color = color;
            return image;
        }

        static void CreateBarOutline(Transform parent)
        {
            CreateBarEdge("Top", parent, new Vector2(0f, 0.5f), new Vector2(1f, 0.5f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Color(0.88f, 0.96f, 1f, 0.72f));
            CreateBarEdge("Bottom", parent, new Vector2(0f, 0.5f), new Vector2(1f, 0.5f), new Vector2(0f, -1f), new Vector2(0f, -1f), new Color(0.88f, 0.96f, 1f, 0.72f));
            CreateBarEdge("Left", parent, new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(1f, 0f), new Vector2(1f, 0f), new Color(0.88f, 0.96f, 1f, 0.72f));
            CreateBarEdge("Right", parent, new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(-1f, 0f), new Vector2(-1f, 0f), new Color(0.88f, 0.96f, 1f, 0.72f));
        }

        static void CreateBarEdge(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax, Color color)
        {
            var edge = CreateBarImage(name, parent, color);
            var rt = edge.rectTransform;
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.offsetMin = offsetMin;
            rt.offsetMax = offsetMax;
        }

        static void PositionHpBarOverHead(GameObject unitGo, Transform barRoot, string unitType)
        {
            if (unitGo == null || barRoot == null) return;

            var renderers = unitGo.GetComponentsInChildren<Renderer>();
            if (renderers == null || renderers.Length == 0)
            {
                barRoot.localPosition = Vector3.up * (2.9f + GetHpBarTypeOffset(unitType));
                return;
            }

            bool hasBounds = false;
            Bounds combined = default;
            foreach (var renderer in renderers)
            {
                if (renderer == null) continue;
                if (!hasBounds)
                {
                    combined = renderer.bounds;
                    hasBounds = true;
                }
                else
                {
                    combined.Encapsulate(renderer.bounds);
                }
            }

            if (!hasBounds)
            {
                barRoot.localPosition = Vector3.up * (2.9f + GetHpBarTypeOffset(unitType));
                return;
            }

            float extraLift = Mathf.Max(0.85f, combined.size.y * 0.35f) + GetHpBarTypeOffset(unitType);
            Vector3 headWorld = new(combined.center.x, combined.max.y + extraLift, combined.center.z);
            barRoot.localPosition = unitGo.transform.InverseTransformPoint(headWorld);
        }

        static float GetHpBarTypeOffset(string unitType)
            => !string.IsNullOrWhiteSpace(unitType) && HpBarTypeOffset.TryGetValue(unitType, out var offset)
                ? offset
                : 0.32f;

        static Transform FindChildRecursive(Transform root, string childName)
        {
            if (root == null || string.IsNullOrWhiteSpace(childName)) return null;
            if (root.name == childName) return root;

            for (int i = 0; i < root.childCount; i++)
            {
                var found = FindChildRecursive(root.GetChild(i), childName);
                if (found != null) return found;
            }

            return null;
        }

        static void UpdateHpBarVisual(UnitView view, float hp01)
        {
            hp01 = Mathf.Clamp01(hp01);
            HpBarVisuals.ApplyFill(view.hpBarFill, view.hpBarImage, hp01);

            if (view.hpBarFillAnchor != null)
                view.hpBarFillAnchor.localScale = Vector3.one;

            if (view.hpBarFill != null)
            {
                view.hpBarFill.localScale = new Vector3(
                    view.hpBarFillBaseScale.x * hp01,
                    view.hpBarFillBaseScale.y,
                    view.hpBarFillBaseScale.z);

                if (view.hpBarFillAnchor != null)
                {
                    view.hpBarFill.localPosition = new Vector3(
                        0.5f * hp01,
                        view.hpBarFillBaseLocalPosition.y,
                        view.hpBarFillBaseLocalPosition.z);
                }
            }
        }

        static void SetAnimIdle(UnitView view)
        {
            if (view?.anim == null) return;
            foreach (var p in view.anim.parameters)
                if (p.name == "Speed" && p.type == AnimatorControllerParameterType.Float)
                { view.anim.SetFloat("Speed", 0f); return; }
            TryCrossFade(view.anim, _idleStates, 0.20f);
        }

        static void PlayAttackVariant(UnitView view)
        {
            if (view?.anim == null || view.anim.runtimeAnimatorController == null)
                return;

            var primary = Animator.StringToHash("Attack1");
            var secondary = Animator.StringToHash("Attack2");
            bool hasPrimary = view.anim.HasState(0, primary);
            bool hasSecondary = view.anim.HasState(0, secondary);

            if (hasPrimary || hasSecondary)
            {
                if (hasPrimary && hasSecondary)
                {
                    int next = view.nextAttackVariant % 2;
                    view.anim.Play(next == 0 ? primary : secondary, 0, 0f);
                    view.nextAttackVariant = (view.nextAttackVariant + 1) % 2;
                    return;
                }

                view.anim.Play(hasPrimary ? primary : secondary, 0, 0f);
                return;
            }

            TryCrossFade(view.anim, _attackStates, 0.10f);
        }

        static void TryCrossFade(Animator anim, string[] states, float transTime)
        {
            if (anim == null || anim.runtimeAnimatorController == null) return;

            foreach (var s in states)
            {
                int hash = Animator.StringToHash(s);
                if (anim.HasState(0, hash))
                {
                    // Use Play (not CrossFade) so the state is entered immediately,
                    // preventing the controller's default-state from overriding us
                    // on freshly instantiated Animators.
                    anim.Play(hash, 0, 0f);
                    return;
                }
            }
        }

        static void UpgradeLegacyRendererMaterials(Renderer renderer)
        {
            if (renderer == null) return;
            var urpLit = Shader.Find("Universal Render Pipeline/Lit");
            if (urpLit == null) return;

            var shared = renderer.sharedMaterials;
            var instanced = renderer.materials;
            bool changed = false;

            for (int i = 0; i < instanced.Length; i++)
            {
                var mat = instanced[i];
                if (mat == null || mat.shader == null) continue;
                if (mat.shader.name.StartsWith("Universal Render Pipeline"))
                    continue;

                var upgraded = new Material(urpLit);
                var source = i < shared.Length ? shared[i] : mat;

                if (source != null)
                {
                    CopyTextureIfPresent(source, upgraded, "_MainTex", "_BaseMap");
                    CopyTextureIfPresent(source, upgraded, "_BumpMap", "_BumpMap");
                    CopyTextureIfPresent(source, upgraded, "_MetallicGlossMap", "_MetallicGlossMap");
                    CopyTextureIfPresent(source, upgraded, "_OcclusionMap", "_OcclusionMap");
                    CopyTextureIfPresent(source, upgraded, "_EmissionMap", "_EmissionMap");

                    if (source.HasProperty("_Color"))
                        upgraded.SetColor("_BaseColor", source.GetColor("_Color"));
                    if (source.HasProperty("_EmissionColor"))
                        upgraded.SetColor("_EmissionColor", source.GetColor("_EmissionColor"));
                    if (source.HasProperty("_Glossiness"))
                        upgraded.SetFloat("_Smoothness", source.GetFloat("_Glossiness"));
                    if (source.HasProperty("_BumpScale"))
                        upgraded.SetFloat("_BumpScale", source.GetFloat("_BumpScale"));
                    if (source.HasProperty("_Metallic"))
                        upgraded.SetFloat("_Metallic", source.GetFloat("_Metallic"));
                    if (source.HasProperty("_OcclusionStrength"))
                        upgraded.SetFloat("_OcclusionStrength", source.GetFloat("_OcclusionStrength"));
                }

                instanced[i] = upgraded;
                changed = true;
            }

            if (changed)
                renderer.materials = instanced;
        }

        static void CopyTextureIfPresent(Material source, Material destination, string sourceProp, string destinationProp)
        {
            if (!source.HasProperty(sourceProp) || !destination.HasProperty(destinationProp))
                return;

            var tex = source.GetTexture(sourceProp);
            if (tex == null)
                return;

            destination.SetTexture(destinationProp, tex);
        }

        IEnumerator PlayDeathThenDestroy(GameObject go, Animator anim)
        {
            if (anim != null)
                TryCrossFade(anim, _deathStates, 0.08f);
            yield return new WaitForSeconds(1.8f);
            if (go != null) Destroy(go);
        }

        void DestroyAll()
        {
            foreach (var kv in _units)
                if (kv.Value?.go != null) Destroy(kv.Value.go);
            _units.Clear();
        }
    }
}
