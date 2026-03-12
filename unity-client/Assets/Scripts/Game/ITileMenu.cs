namespace CastleDefender.Game
{
    /// <summary>
    /// Decouples TileGrid (Game assembly) from TileMenuUI (UI assembly).
    /// TileMenuUI implements this interface; TileGrid holds an ITileMenu reference.
    /// </summary>
    public interface ITileMenu
    {
        void Show(int col, int row, string tileType, string towerType);
    }
}
