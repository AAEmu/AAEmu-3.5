namespace AAEmu.Game.Models.Game.Items.Actions;

public enum ItemTaskLogType : byte
{
    UpdateOnly = 0,
    GainItem = 1,
    RemoveItem = 2,
    MoveItem = 3,
    SwapItem = 4,
    Place = 5
}
