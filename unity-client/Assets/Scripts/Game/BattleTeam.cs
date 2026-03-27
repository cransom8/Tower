using UnityEngine;

namespace CastleDefender.Game
{
    public enum BattleTeam
    {
        Red,
        Blue,
        Yellow,
        Green
    }

    public interface ITeamOwned
    {
        BattleTeam Team { get; }
    }

    public static class BattleTeamUtility
    {
        public static bool AreFriendly(BattleTeam a, BattleTeam b) => a == b;

        public static bool AreEnemies(BattleTeam a, BattleTeam b) => a != b;

        public static string ToServerTeamKey(BattleTeam team)
        {
            return team switch
            {
                BattleTeam.Red => "red",
                BattleTeam.Blue => "blue",
                BattleTeam.Yellow => "yellow",
                BattleTeam.Green => "green",
                _ => "red"
            };
        }

        public static string NormalizeServerTeamKey(string teamKey)
        {
            if (string.IsNullOrWhiteSpace(teamKey))
                return null;

            string normalized = teamKey.Trim().ToLowerInvariant();
            return normalized switch
            {
                "gold" => "yellow",
                _ => normalized,
            };
        }

        public static bool MatchesServerTeamKey(BattleTeam team, string teamKey)
        {
            return string.Equals(
                ToServerTeamKey(team),
                NormalizeServerTeamKey(teamKey),
                System.StringComparison.OrdinalIgnoreCase);
        }

        public static bool TryParseServerTeamKey(string teamKey, out BattleTeam team)
        {
            switch (NormalizeServerTeamKey(teamKey))
            {
                case "red":
                    team = BattleTeam.Red;
                    return true;
                case "blue":
                    team = BattleTeam.Blue;
                    return true;
                case "yellow":
                    team = BattleTeam.Yellow;
                    return true;
                case "green":
                    team = BattleTeam.Green;
                    return true;
                default:
                    team = BattleTeam.Red;
                    return false;
            }
        }

        public static Color ToColor(BattleTeam team)
        {
            return team switch
            {
                BattleTeam.Red => new Color(0.86f, 0.25f, 0.22f),
                BattleTeam.Blue => new Color(0.24f, 0.50f, 0.92f),
                BattleTeam.Yellow => new Color(0.92f, 0.74f, 0.20f),
                BattleTeam.Green => new Color(0.20f, 0.72f, 0.42f),
                _ => Color.white
            };
        }

        public static Color ToDebugColor(BattleTeam team) => ToColor(team);
    }
}
