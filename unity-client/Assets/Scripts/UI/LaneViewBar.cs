// LaneViewBar.cs — Top-left row of lane-pan buttons.
// One button per active player lane. Clicking smoothly pans the camera to that lane.
//
// SETUP (Game_ML.unity):
//   Canvas
//   └── LaneViewBar  (this component, anchored top-left)
//       ├── Btn_Lane0  (Button + Image + TMP_Text child)
//       ├── Btn_Lane1
//       ├── Btn_Lane2
//       └── Btn_Lane3
//
// Assign the 4 buttons to LaneButtons[] in the Inspector.
// Assign optional TMP_Text children to LaneLabels[].

using UnityEngine;
using UnityEngine.UI;
using TMPro;
using CastleDefender.Game;
using CastleDefender.Net;

namespace CastleDefender.UI
{
    public class LaneViewBar : MonoBehaviour
    {
        [Header("Lane Buttons (0=Red 1=Gold 2=Blue 3=Green)")]
        public Button[]   LaneButtons;
        public TMP_Text[] LaneLabels;   // optional — one per button

        [Header("Camera Pan")]
        public float CamHeight   = 20f;
        public float ZOffset     = -10f;

        [Header("Button Colors")]
        public Color ColorViewing  = new Color(1.00f, 0.85f, 0.20f); // bright gold = currently viewing
        public Color ColorMine     = new Color(0.25f, 0.70f, 0.30f); // green tint  = my lane
        public Color ColorOther    = new Color(0.20f, 0.20f, 0.25f); // dark        = other lane
        public Color ColorInactive = new Color(0.10f, 0.10f, 0.12f, 0.4f);

        // ── Lane identity ─────────────────────────────────────────────────────
        static readonly string[] Labels = { "R", "G", "B", "Gr" };

        static readonly Color[] LaneTints =
        {
            new Color(0.85f, 0.20f, 0.20f), // Red
            new Color(0.85f, 0.70f, 0.10f), // Gold
            new Color(0.20f, 0.45f, 0.85f), // Blue
            new Color(0.20f, 0.65f, 0.25f), // Green
        };

        // ── State ─────────────────────────────────────────────────────────────
        int              _viewingLane = -1;   // -1 = follow my lane
        CameraController _camCtrl;

        // ─────────────────────────────────────────────────────────────────────
        void Start()
        {
            _camCtrl = FindFirstObjectByType<CameraController>();

            for (int i = 0; i < LaneButtons.Length; i++)
            {
                int lane = i;
                LaneButtons[i].onClick.AddListener(() => OnLaneClicked(lane));

                if (LaneLabels != null && i < LaneLabels.Length && LaneLabels[i] != null)
                    LaneLabels[i].text = Labels[i];
            }
        }

        void Update() => RefreshColors();

        // ── Button state ──────────────────────────────────────────────────────
        void RefreshColors()
        {
            var sa     = SnapshotApplier.Instance;
            int myLane = NetworkManager.Instance != null
                       ? NetworkManager.Instance.MyLaneIndex : 0;
            int viewing = _viewingLane >= 0 ? _viewingLane : myLane;

            for (int i = 0; i < LaneButtons.Length; i++)
            {
                bool active = sa == null || sa.GetLane(i) != null;
                LaneButtons[i].gameObject.SetActive(active);
                if (!active) continue;

                Color c = i == viewing  ? ColorViewing
                        : i == myLane   ? ColorMine
                        : ColorOther;
                LaneButtons[i].image.color = c;
            }
        }

        // ── Camera pan ────────────────────────────────────────────────────────
        void OnLaneClicked(int laneIndex)
        {
            _viewingLane = laneIndex;

            Vector3 castlePos  = TileGrid.TileToWorld(laneIndex, 5, 27);
            Vector3 spawnPos   = TileGrid.TileToWorld(laneIndex, 5,  0);
            Vector3 laneCenter = (castlePos + spawnPos) * 0.5f + new Vector3(0f, 0f, ZOffset);
            Vector3 target     = laneCenter + Vector3.up * CamHeight;

            // Route through CameraController so its smoothing drives the pan and
            // the two systems don't fight over the camera transform.
            if (_camCtrl != null)
            {
                _camCtrl.PanTo(target);
            }
            else
            {
                // Fallback: direct move (CameraController not present)
                var cam = Camera.main;
                if (cam != null) cam.transform.position = target;
            }

            // Also tell SnapshotApplier which lane to render (so tiles/units update)
            if (SnapshotApplier.Instance != null)
                SnapshotApplier.Instance.ViewingLane = laneIndex;
        }
    }
}
