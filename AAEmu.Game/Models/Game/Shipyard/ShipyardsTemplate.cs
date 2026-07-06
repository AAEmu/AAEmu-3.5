namespace AAEmu.Game.Models.Game.Shipyard;

/// <summary>
/// Шаблон верфи из таблицы <c>shipyards</c> БД <c>compact.sqlite3</c>.
/// </summary>
public class ShipyardsTemplate
{
    /// <summary>Идентификатор верфи.</summary>
    public uint Id { get; set; }

    /// <summary>Название верфи.</summary>
    public string Name { get; set; }

    /// <summary>Идентификатор основной модели.</summary>
    public uint MainModelId { get; set; }

    /// <summary>Идентификатор результирующего предмета (корабля).</summary>
    public uint ItemId { get; set; }

    /// <summary>Время церемониальной анимации запуска корабля.</summary>
    public int CeremonyAnimTime { get; set; }

    /// <summary>Ключ церемониальной анимации.</summary>
    public string CeremonyAnimKey { get; set; }

    /// <summary>Идентификатор модели, используемой во время церемонии запуска.</summary>
    public uint CeremonyModelId { get; set; }

    /// <summary>Смещение точки появления корабля вперёд от верфи.</summary>
    public float SpawnOffsetFront { get; set; }

    /// <summary>Смещение точки появления корабля по высоте.</summary>
    public float SpawnOffsetZ { get; set; }

    /// <summary>Радиус зоны постройки.</summary>
    public int BuildRadius { get; set; }

    /// <summary>Длительность действия налога (в секундах).</summary>
    public int TaxDuration { get; set; }

    /// <summary>Идентификатор исходного предмета-чертежа верфи.</summary>
    public uint OriginItemId { get; set; }

    /// <summary>Идентификатор налоговой ставки (<see cref="Taxation.Id"/>).</summary>
    public int TaxationId { get; set; }

    /// <summary>Этапы строительства верфи (из <c>shipyard_steps</c>).</summary>
    public Dictionary<int, ShipyardSteps> ShipyardSteps { get; set; } = [];
}
