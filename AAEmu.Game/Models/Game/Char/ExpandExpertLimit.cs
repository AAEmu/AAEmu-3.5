namespace AAEmu.Game.Models.Game.Char;

/// <summary>
/// Расширение лимита экспертизы из таблицы <c>expand_expert_limits</c> БД <c>compact.sqlite3</c>.
/// </summary>
public class ExpandExpertLimit
{
    /// <summary>Идентификатор записи (искусственный, таблица не имеет собственного ключа).</summary>
    public uint Id { get; set; }

    /// <summary>Количество расширяемых слотов.</summary>
    public byte ExpandCount { get; set; }

    /// <summary>Затраты жизненной силы (life point).</summary>
    public int LifePoint { get; set; }

    /// <summary>Идентификатор предмета, необходимого для расширения.</summary>
    public uint ItemId { get; set; }

    /// <summary>Количество предмета.</summary>
    public int ItemCount { get; set; }
}
