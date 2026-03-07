// ClassicGameManager.cs — Classic 2P game scene (state_snapshot driven).
//
// SETUP (Game_Classic.unity):
//   Attach to a "GameManager" GameObject.
//   Inspector:
//     UnitPrefab         — capsule placeholder
//     Camera             — main camera
//     Txt_BottomLives / Gold / Income   — HUD TMP_Text refs
//     Txt_TopLives / Gold / Income
//     Txt_MySide         — shows "You are: Bottom" etc.
//
// Lane layout (2.5D top-down):
//   Bottom player: spawn at WorldZ 0, castle at WorldZ 20
//   Top    player: spawn at WorldZ 20, castle at WorldZ 0

using System.Collections.Generic;
using UnityEngine;
using CastleDefender.Net;

namespace CastleDefender.Game
{
    public class ClassicGameManager : MonoBehaviour
    {
        // ── Inspector ─────────────────────────────────────────────────────────
        [Header("Unit prefab")]
        public GameObject UnitPrefab;

        [Header("Lane layout")]
        public float LaneLength  = 20f;
        public float LaneOffsetX = 1.5f;

        [Header("HUD — Bottom player")]
        public TMPro.TMP_Text TxtBottomLives;
        public TMPro.TMP_Text TxtBottomGold;
        public TMPro.TMP_Text TxtBottomIncome;

        [Header("HUD — Top player")]
        public TMPro.TMP_Text TxtTopLives;
        public TMPro.TMP_Text TxtTopGold;
        public TMPro.TMP_Text TxtTopIncome;

        [Header("Side indicator")]
        public TMPro.TMP_Text TxtMySide;

        [Header("Spawn buttons (Bottom player only)")]
        public UnityEngine.UI.Button[] SpawnButtons;

        // ── Constants ─────────────────────────────────────────────────────────
        static readonly string[] UnitTypes   = { "runner", "footman", "ironclad", "warlock", "golem" };
        static readonly int[]    UnitCosts   = { 8, 10, 16, 18, 25 };
        static readonly Color    ColorBottom = new Color(0.20f, 0.80f, 0.70f);
        static readonly Color    ColorTop    = new Color(0.90f, 0.25f, 0.25f);
        const float MoveLerpDuration = 0.14f;

        // ── Runtime state ─────────────────────────────────────────────────────
        class UnitView
        {
            public GameObject go;
            public Vector3    moveFrom;
            public Vector3    moveTo;
            public float      lerpT;
        }

        readonly Dictionary<string, UnitView> _unitViews = new();
        string _mySide;

        // ─────────────────────────────────────────────────────────────────────
        void OnEnable()
        {
            var sa = SnapshotApplier.Instance;
            if (sa != null) sa.OnClassicSnapshotApplied += OnSnapshot;
        }

        void OnDisable()
        {
            var sa = SnapshotApplier.Instance;
            if (sa != null) sa.OnClassicSnapshotApplied -= OnSnapshot;
        }

        void Start()
        {
            _mySide = NetworkManager.Instance?.MySide ?? "bottom";

            if (TxtMySide != null)
                TxtMySide.text = $"You are: {Capitalize(_mySide)}";

            bool isBottom = _mySide == "bottom";
            for (int i = 0; i < SpawnButtons.Length; i++)
            {
                SpawnButtons[i].gameObject.SetActive(isBottom);
                int captured = i;
                SpawnButtons[i].onClick.AddListener(() =>
                    ActionSender.ClassicSpawnUnit(UnitTypes[captured]));
            }
        }

        void Update()
        {
            // Smooth unit movement
            float dt = Time.deltaTime;
            foreach (var view in _unitViews.Values)
            {
                if (view?.go == null) continue;
                view.lerpT = Mathf.Min(1f, view.lerpT + dt / MoveLerpDuration);
                view.go.transform.position = Vector3.Lerp(view.moveFrom, view.moveTo, view.lerpT);
            }

            // Dim spawn buttons by affordability
            var snap = SnapshotApplier.Instance?.LatestClassic;
            if (snap?.players == null) return;
            var myState = _mySide == "bottom" ? snap.players.bottom : snap.players.top;
            if (myState == null) return;
            for (int i = 0; i < SpawnButtons.Length; i++)
                if (SpawnButtons[i].gameObject.activeSelf)
                    SpawnButtons[i].image.color = myState.gold >= UnitCosts[i]
                        ? Color.white
                        : new Color(0.5f, 0.5f, 0.5f, 0.6f);
        }

        // ─────────────────────────────────────────────────────────────────────
        void OnSnapshot(ClassicSnapshot snap)
        {
            UpdateHUD(snap);
            SyncUnits(snap);
        }

        void UpdateHUD(ClassicSnapshot snap)
        {
            if (snap.players == null) return;
            var b = snap.players.bottom;
            var t = snap.players.top;

            if (b != null)
            {
                if (TxtBottomLives  != null) TxtBottomLives.text  = $"❤ {b.lives}";
                if (TxtBottomGold   != null) TxtBottomGold.text   = $"⬡ {Mathf.FloorToInt(b.gold)}";
                if (TxtBottomIncome != null) TxtBottomIncome.text = $"↑ {b.income:0.0}/s";
            }
            if (t != null)
            {
                if (TxtTopLives  != null) TxtTopLives.text  = $"❤ {t.lives}";
                if (TxtTopGold   != null) TxtTopGold.text   = $"⬡ {Mathf.FloorToInt(t.gold)}";
                if (TxtTopIncome != null) TxtTopIncome.text = $"↑ {t.income:0.0}/s";
            }
        }

        void SyncUnits(ClassicSnapshot snap)
        {
            var seen = new HashSet<string>();

            if (snap.units != null)
            {
                foreach (var u in snap.units)
                {
                    seen.Add(u.id);

                    if (!_unitViews.TryGetValue(u.id, out var view) || view.go == null)
                        view = CreateUnit(u);

                    Vector3 target = UnitWorldPos(u.side, u.y);
                    view.moveFrom = view.go.transform.position;
                    view.moveTo   = target;
                    view.lerpT    = 0f;
                }
            }

            var toRemove = new List<string>();
            foreach (var kv in _unitViews)
                if (!seen.Contains(kv.Key)) toRemove.Add(kv.Key);

            foreach (var id in toRemove)
            {
                if (_unitViews[id]?.go != null) Destroy(_unitViews[id].go);
                _unitViews.Remove(id);
            }
        }

        UnitView CreateUnit(ClassicUnit u)
        {
            if (UnitPrefab == null) return new UnitView();

            Vector3 spawnPos = UnitWorldPos(u.side, u.y);
            var go = Instantiate(UnitPrefab, spawnPos, Quaternion.identity, transform);
            go.name = $"Unit_{u.id}_{u.type}";

            float scale = u.type switch
            {
                "runner"   => 0.6f,
                "ironclad" => 1.0f,
                "warlock"  => 0.85f,
                "golem"    => 1.3f,
                _          => 0.8f,
            };
            go.transform.localScale = Vector3.one * scale;

            Color col = u.side == _mySide ? ColorBottom : ColorTop;
            foreach (var r in go.GetComponentsInChildren<Renderer>())
                r.material.color = col;

            var view = new UnitView { go = go, moveFrom = spawnPos, moveTo = spawnPos, lerpT = 1f };
            _unitViews[u.id] = view;
            return view;
        }

        Vector3 UnitWorldPos(string side, float y)
        {
            float z = side == "bottom"
                ? Mathf.Lerp(0f, LaneLength, y)
                : Mathf.Lerp(LaneLength, 0f, y);
            float x = side == "bottom" ? -LaneOffsetX : LaneOffsetX;
            return new Vector3(x, 0f, z);
        }

        static string Capitalize(string s)
            => string.IsNullOrEmpty(s) ? s : char.ToUpper(s[0]) + s.Substring(1);
    }
}
