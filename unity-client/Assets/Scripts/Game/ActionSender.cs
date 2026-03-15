// ActionSender.cs - wraps socket.emit("player_action", ...) for all game actions.
// Matches server event names and payload schemas exactly.
//
// Usage:
//   ActionSender.PlaceUnit(col, row, "goblin");
//   ActionSender.SpawnUnit("footman");
//   ActionSender.SetAutosend(true, enabledUnits, loadoutKeys);

using System.Collections.Generic;
using UnityEngine;
using CastleDefender.Net;

namespace CastleDefender.Game
{
    public static class ActionSender
    {
        [System.Serializable]
        sealed class ActionEnvelope
        {
            public string type;
            public object data;
        }

        [System.Serializable]
        sealed class PlaceUnitPayload
        {
            public int gridX;
            public int gridY;
            public string unitTypeKey;
        }

        [System.Serializable]
        sealed class SellTowerPayload
        {
            public int gridX;
            public int gridY;
        }

        [System.Serializable]
        sealed class UpgradeTowerPayload
        {
            public int gridX;
            public int gridY;
            public int x;
            public int y;
            public int col;
            public int row;
            public string towerType;
        }

        [System.Serializable]
        sealed class EmptyPayload
        {
        }

        [System.Serializable]
        sealed class SpawnUnitPayload
        {
            public string unitType;
        }

        [System.Serializable]
        sealed class AutosendPayload
        {
            public bool enabled;
            public Dictionary<string, bool> enabledUnits;
            public string[] loadoutKeys;
        }

        static void SendAction(string type, object data)
        {
            Debug.Log($"[ActionSender] SendAction type={type} connected={NetworkManager.Instance?.IsConnected}");
            NetworkManager.Instance.Emit("player_action", new ActionEnvelope { type = type, data = data });
        }

        public static void PlaceUnit(int col, int row, string unitTypeKey)
        {
            Debug.Log($"[ActionSender] PlaceUnit col={col} row={row} unitTypeKey={unitTypeKey}");
            SendAction("place_unit", new PlaceUnitPayload
            {
                gridX = col,
                gridY = row,
                unitTypeKey = unitTypeKey
            });
        }

        public static void SellTower(int col, int row)
            => SendAction("sell_tower", new SellTowerPayload { gridX = col, gridY = row });

        public static void UpgradeTower(int col, int row, string towerType = null)
            => SendAction("upgrade_tower", new UpgradeTowerPayload
            {
                gridX = col,
                gridY = row,
                x = col,
                y = row,
                col = col,
                row = row,
                towerType = towerType
            });

        public static void UpgradeBarracks()
            => SendAction("upgrade_barracks", new EmptyPayload());

        public static void SpawnUnit(string unitType)
            => SendAction("spawn_unit", new SpawnUnitPayload { unitType = unitType });

        public static void SetAutosend(bool enabled, Dictionary<string, bool> enabledUnits, string[] loadoutKeys)
            => SendAction("set_autosend", new AutosendPayload
            {
                enabled = enabled,
                enabledUnits = enabledUnits,
                loadoutKeys = loadoutKeys
            });

        public static void ClassicSpawnUnit(string unitType)
            => SendAction("spawn_unit", new SpawnUnitPayload { unitType = unitType });

        public static void ClassicBuildTower(string slot, string towerType)
            => SendAction("build_tower", new { slot, towerType });

        public static void ClassicUpgradeTower(string slot)
            => SendAction("upgrade_tower", new { slot });

        public static void ClassicSellTower(string slot)
            => SendAction("sell_tower", new { slot });

        public static void CreateMLRoom(string displayName = "Player")
            => NetworkManager.Instance.Emit("create_ml_room", new { displayName });

        public static void JoinMLRoom(string code, string displayName = "Player")
            => NetworkManager.Instance.Emit("join_ml_room",
               new { code = code.ToUpper(), displayName });

        public static void MLPlayerReady()
            => NetworkManager.Instance.Emit("ml_player_ready", new { });

        public static void MLForceStart()
            => NetworkManager.Instance.Emit("ml_force_start", new { });

        public static void AddAI(string difficulty)
            => NetworkManager.Instance.Emit("add_ai_to_ml_room", new { difficulty });

        public static void RemoveAI(int laneIndex)
            => NetworkManager.Instance.Emit("remove_ai_from_ml_room", new { laneIndex });

        public static void CreateClassicRoom()
            => NetworkManager.Instance.Emit("create_room", null);

        public static void JoinClassicRoom(string code)
            => NetworkManager.Instance.Emit("join_room", new { code = code.ToUpper() });

        public static void RequestRematch()
            => NetworkManager.Instance.Emit("request_rematch", null);

        public static void CancelRematch()
            => NetworkManager.Instance.Emit("cancel_rematch", null);

        public static void QueueEnter(string gameType, string matchFormat, bool ranked, int[] unitTypeIds = null)
            => NetworkManager.Instance.Emit("queue:enter_v2",
               new { gameType, matchFormat, ranked, unitTypeIds });

        public static void QueueLeave()
            => NetworkManager.Instance.Emit("queue:leave", null);

        public static void LobbyCreate(string gameType, string matchFormat, string pvpMode = "teams", string displayName = "Player", int[] unitTypeIds = null)
            => NetworkManager.Instance.Emit("lobby:create",
               new { gameType, matchFormat, pvpMode, displayName, unitTypeIds });

        public static void LobbyJoin(string code, string displayName = "Player")
            => NetworkManager.Instance.Emit("lobby:join",
               new { code = code.ToUpper(), displayName });

        public static void LobbyReady(bool ready)
            => NetworkManager.Instance.Emit("lobby:ready", new { ready });

        public static void LobbyLeave()
            => NetworkManager.Instance.Emit("lobby:leave", null);

        public static void LobbyLaunch(int[] unitTypeIds = null)
            => NetworkManager.Instance.Emit("lobby:launch", new { unitTypeIds });

        public static void LobbyAddBot(string difficulty = "medium")
            => NetworkManager.Instance.Emit("lobby:add_bot", new { difficulty });

        public static void LobbyRemoveBot(int index)
            => NetworkManager.Instance.Emit("lobby:remove_bot", new { index });
    }
}
