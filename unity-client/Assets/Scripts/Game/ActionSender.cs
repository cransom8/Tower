// ActionSender.cs — wraps socket.emit("player_action", ...) for all game actions.
// Matches server event names and payload schemas exactly.
//
// Usage:
//   ActionSender.PlaceWall(col, row);
//   ActionSender.SpawnUnit("footman");
//   ActionSender.SetAutosend(true, enabledUnits, "normal");

using System.Collections.Generic;
using UnityEngine;
using CastleDefender.Net;

namespace CastleDefender.Game
{
    public static class ActionSender
    {
        // ── Helpers ───────────────────────────────────────────────────────────
        // Server destructures { type, data } from player_action payload.
        // Session-based routing handles code/side/laneIndex server-side.
        static void SendAction(string type, object data)
        {
            Debug.Log($"[ActionSender] SendAction type={type} connected={NetworkManager.Instance?.IsConnected}");
            NetworkManager.Instance.Emit("player_action", new { type, data });
        }

        // ── ML Actions ────────────────────────────────────────────────────────

        public static void PlaceWall(int col, int row)
            => SendAction("place_wall", new { gridX = col, gridY = row });

        public static void RemoveWall(int col, int row)
            => SendAction("remove_wall", new { gridX = col, gridY = row });

        /// <param name="unitTypeKey">archer|fighter|mage|ballista|cannon</param>
        public static void UpgradeWall(int col, int row, string unitTypeKey)
            => SendAction("upgrade_wall", new { x = col, y = row, unitTypeKey });

        public static void UpgradeTower(int col, int row, string towerType = null)
            => SendAction("upgrade_tower", new
            {
                gridX = col,
                gridY = row,
                x = col,
                y = row,
                col,
                row,
                towerType
            });

        public static void UpgradeBarracks()
            => SendAction("upgrade_barracks", new { });

        /// <param name="unitType">runner|footman|ironclad|warlock|golem</param>
        public static void SpawnUnit(string unitType)
            => SendAction("spawn_unit", new { unitType });

        /// <param name="enabledUnits">unitType → bool</param>
        /// <param name="rate">slow|normal|fast</param>
        public static void SetAutosend(bool enabled, Dictionary<string, bool> enabledUnits, string rate)
            => SendAction("set_autosend", new { enabled, enabledUnits, rate });

        // ── Classic Actions ───────────────────────────────────────────────────
        // Server uses session.side to determine whose action; just send type+data.

        public static void ClassicSpawnUnit(string unitType)
            => SendAction("spawn_unit", new { unitType });

        public static void ClassicBuildTower(string slot, string towerType)
            => SendAction("build_tower", new { slot, towerType });

        public static void ClassicUpgradeTower(string slot)
            => SendAction("upgrade_tower", new { slot });

        public static void ClassicSellTower(string slot)
            => SendAction("sell_tower", new { slot });

        // ── ML Lobby ──────────────────────────────────────────────────────────

        public static void CreateMLRoom(string displayName = "Player")
            => NetworkManager.Instance.Emit("create_ml_room", new { displayName });

        public static void JoinMLRoom(string code, string displayName = "Player")
            => NetworkManager.Instance.Emit("join_ml_room",
               new { code = code.ToUpper(), displayName });

        /// <summary>Signal this player is ready to start.</summary>
        public static void MLPlayerReady()
            => NetworkManager.Instance.Emit("ml_player_ready", new { });

        /// <summary>Host-only: force-start the ML game (bypasses ready check if ≥2 total).</summary>
        public static void MLForceStart()
            => NetworkManager.Instance.Emit("ml_force_start", new { });

        public static void AddAI(string difficulty)
            => NetworkManager.Instance.Emit("add_ai_to_ml_room", new { difficulty });

        public static void RemoveAI(int laneIndex)
            => NetworkManager.Instance.Emit("remove_ai_from_ml_room", new { laneIndex });

        // ── Classic Lobby ─────────────────────────────────────────────────────

        public static void CreateClassicRoom()
            => NetworkManager.Instance.Emit("create_room", null);

        public static void JoinClassicRoom(string code)
            => NetworkManager.Instance.Emit("join_room", new { code = code.ToUpper() });

        // ── Shared ────────────────────────────────────────────────────────────

        public static void RequestRematch()
            => NetworkManager.Instance.Emit("request_rematch", null);

        // ── Queue System (Phase U5) ───────────────────────────────────────────

        /// <param name="gameType">line_wars|survival</param>
        /// <param name="matchFormat">1v1|2v2|ffa</param>
        public static void QueueEnter(string gameType, string matchFormat, bool ranked, int? loadoutSlot = null)
            => NetworkManager.Instance.Emit("queue:enter_v2",
               new { gameType, matchFormat, ranked, loadoutSlot });

        public static void QueueLeave()
            => NetworkManager.Instance.Emit("queue:leave", null);

        // ── Private Lobby System (Phase U5) ──────────────────────────────────

        public static void LobbyCreate(string gameType, string matchFormat, string pvpMode = "teams", string displayName = "Player")
            => NetworkManager.Instance.Emit("lobby:create",
               new { gameType, matchFormat, pvpMode, displayName });

        public static void LobbyJoin(string code, string displayName = "Player")
            => NetworkManager.Instance.Emit("lobby:join",
               new { code = code.ToUpper(), displayName });

        public static void LobbyReady(bool ready)
            => NetworkManager.Instance.Emit("lobby:ready", new { ready });

        public static void LobbyLeave()
            => NetworkManager.Instance.Emit("lobby:leave", null);

        public static void LobbyLaunch(int? loadoutSlot = null)
            => NetworkManager.Instance.Emit("lobby:launch", new { loadoutSlot });

        public static void LobbyAddBot(string difficulty = "medium")
            => NetworkManager.Instance.Emit("lobby:add_bot", new { difficulty });

        public static void LobbyRemoveBot(int index)
            => NetworkManager.Instance.Emit("lobby:remove_bot", new { index });
    }
}
