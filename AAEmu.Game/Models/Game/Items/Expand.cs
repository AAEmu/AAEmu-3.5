namespace AAEmu.Game.Models.Game.Items;

/// <summary>
/// Параметры расширения инвентаря/банка из таблицы <c>bag_expands</c> БД <c>compact.sqlite3</c>.
/// </summary>
public class Expand
{
    /// <summary>Является ли расширение банковским (<c>is_bank</c>).</summary>
    public bool IsBank { get; set; }

    /// <summary>Номер шага расширения.</summary>
    public int Step { get; set; }

    /// <summary>Цена расширения.</summary>
    public int Price { get; set; }

    /// <summary>Идентификатор альтернативного предмета оплаты.</summary>
    public uint ItemId { get; set; }

    /// <summary>Количество альтернативного предмета оплаты.</summary>
    public int ItemCount { get; set; }

    /// <summary>Идентификатор валюты оплаты.</summary>
    public int CurrencyId { get; set; }
}
