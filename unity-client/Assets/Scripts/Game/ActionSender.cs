// ActionSender.cs - wraps socket.emit("player_action", ...) for all game actions.
// Matches server event names and payload schemas exactly.
//
// Fortress-mode unit flow now comes from pads, barracks sites, and lane commands.

using UnityEngine;
using CastleDefender.Net;

namespace CastleDefender.Game
{
    public static class ActionSender
    {
        static void SendAction(string type, object data = null)
        {
            Debug.Log($"[ActionSender] SendAction type={type} connected={NetworkManager.Instance?.IsConnected}");
            object payload = data == null
                ? new { type }
                : new { type, data };
            NetworkManager.Instance?.Emit("player_action", payload);
        }

        public static void BuildOnPad(string padId)
            => SendAction("build_on_pad", new { padId });

        public static void UpgradeBuilding(string padId, string buildingType = null)
            => SendAction("upgrade_building", new { padId, buildingType });

        public static void BuildBarracksSite(string barracksId)
            => SendAction("build_barracks_site", new { barracksId });

        public static void UpgradeBarracksSite(string barracksId)
            => SendAction("upgrade_barracks_site", new { barracksId });

        public static void BuyBarracksUnit(string rosterKey, string barracksId = null, int count = 1)
        {
            if (string.IsNullOrWhiteSpace(barracksId))
            {
                Debug.LogError(
                    $"[BarracksTrace][ClientBuy] Refusing to send buy_barracks_unit for rosterKey='{rosterKey}' " +
                    "because barracksId is missing.");
                return;
            }

            count = Mathf.Max(1, count);
            Debug.Log(
                $"[BarracksTrace][ClientBuy] rosterKey='{rosterKey}' barracksId='{barracksId}' count={count}");
            SendAction("buy_barracks_unit", new { rosterKey, barracksId, count });
        }

        public static void SellBarracksUnit(string rosterKey, string barracksId = null)
        {
            if (string.IsNullOrWhiteSpace(barracksId))
            {
                Debug.LogError(
                    $"[BarracksTrace][ClientBuy] Refusing to send sell_barracks_unit for rosterKey='{rosterKey}' " +
                    "because barracksId is missing.");
                return;
            }

            Debug.Log(
                $"[BarracksTrace][ClientBuy] sell rosterKey='{rosterKey}' barracksId='{barracksId}'");
            SendAction("sell_barracks_unit", new { rosterKey, barracksId });
        }

        public static void DeployBarracksHero(string heroKey, string barracksId = null)
        {
            if (string.IsNullOrWhiteSpace(barracksId))
            {
                Debug.LogError(
                    $"[BarracksTrace][ClientHero] Refusing to send deploy_barracks_hero for heroKey='{heroKey}' " +
                    "because barracksId is missing.");
                return;
            }

            Debug.Log(
                $"[BarracksTrace][ClientHero] heroKey='{heroKey}' barracksId='{barracksId}'");
            SendAction("deploy_barracks_hero", new { heroKey, barracksId });
        }

        public static void SetLaneAttack()
            => SendAction("set_lane_attack");

        public static void SetLaneDefend()
            => SendAction("set_lane_defend");

        public static void SetLaneDefendProgress(float progress)
            => SendAction("set_lane_defend_point", new { progress });

        public static void SetLaneDefendAt(float worldX, float worldY)
            => SendAction("set_lane_defend_point", new { worldX, worldY });

        public static void SetLaneRetreat()
            => SendAction("set_lane_retreat");

        public static void SetLaneRetreatProgress(float progress)
            => SendAction("set_lane_retreat", new { progress });

        public static void CreateMLRoom(string displayName = "Player")
            => NetworkManager.Instance.Emit("create_ml_room", new { displayName });

        public static void JoinMLRoom(string code, string displayName = "Player")
            => NetworkManager.Instance.Emit("join_ml_room",
               new { code = code.ToUpper(), displayName });

        public static void MLPlayerReady()
            => NetworkManager.Instance.Emit("ml_player_ready", new { });

        public static void MLForceStart()
            => NetworkManager.Instance.Emit("ml_force_start", new { });

        public static void RequestStartWaveVote()
        {
            Debug.Log("[WaveStart][Client] emitting ml_start_wave_vote");
            NetworkManager.Instance.Emit("ml_start_wave_vote", null);
        }

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

        public static void ContinueAfterWin()
            => NetworkManager.Instance.Emit("ml_continue_after_win", null);

        public static void EndGameNow()
            => NetworkManager.Instance.Emit("ml_end_game_now", null);

        public static void QueueEnter(string gameType, string matchFormat, bool ranked, int[] unitTypeIds = null)
            => NetworkManager.Instance.Emit("queue:enter_v2",
               new { gameType, matchFormat, ranked, unitTypeIds });

        public static void QueueLeave()
            => NetworkManager.Instance.Emit("queue:leave", null);

        public static void LobbyCreate(string gameType, string matchFormat, string pvpMode = "ffa", string displayName = "Player", int[] unitTypeIds = null)
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
