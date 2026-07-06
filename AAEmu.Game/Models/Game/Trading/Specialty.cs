namespace AAEmu.Game.Models.Game.Trading;

/// <summary>
/// Запись специальной торговли из таблицы <c>specialties</c> БД <c>compact.sqlite3</c>.
/// </summary>
public class Specialty
{
    /// <summary>Идентификатор записи.</summary>
    public uint Id { get; set; }

    /// <summary>Идентификатор группы зоны отправления (строка матрицы).</summary>
    public uint RowZoneGroupId { get; set; }

    /// <summary>Идентификатор группы зоны назначения (колонка матрицы).</summary>
    public uint ColZoneGroupId { get; set; }

    /// <summary>Базовое соотношение цены.</summary>
    public uint Ratio { get; set; }

    /// <summary>Базовая прибыль от продажи.</summary>
    public uint Profit { get; set; }

    /// <summary>Указывает, доступен ли торговый автомат / продавец для данного направления.</summary>
    public bool VendorExist { get; set; }
}
