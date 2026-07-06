namespace AAEmu.Game.Models.Game.Shipyard;

/// <summary>
/// Этап строительства верфи из таблицы <c>shipyard_steps</c> БД <c>compact.sqlite3</c>.
/// </summary>
public class ShipyardSteps
{
    /// <summary>Идентификатор записи.</summary>
    public uint Id { get; set; }

    /// <summary>Идентификатор верфи-владельца.</summary>
    public uint ShipyardId { get; set; }

    /// <summary>Порядковый номер этапа.</summary>
    public int Step { get; set; }

    /// <summary>Идентификатор модели на данном этапе.</summary>
    public uint ModelId { get; set; }

    /// <summary>Идентификатор навыка, используемого на этапе.</summary>
    public uint SkillId { get; set; }

    /// <summary>Количество действий, необходимых для завершения этапа.</summary>
    public int NumActions { get; set; }

    /// <summary>Максимальное здоровье объекта этапа.</summary>
    public int MaxHp { get; set; }
}
