#nullable enable

using Microsoft.Data.Sqlite;

namespace AAEmu.Game.Physics.Static;

/// <summary>Immutable ship-blocking templates and phase models from the current client's SQLite data.</summary>
public sealed class ShipDoodadModelCatalog
{
    private sealed record Model(string Uri, Dictionary<uint, string> Phases);

    private readonly Dictionary<uint, Model> _models;

    private ShipDoodadModelCatalog(Dictionary<uint, Model> models) => _models = models;

    public static ShipDoodadModelCatalog Empty { get; } = new([]);
    public int Count => _models.Count;

    /// <summary>Use the already-open game-data connection before spawning world doodads.</summary>
    public static ShipDoodadModelCatalog Load(SqliteConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);
        var models = new Dictionary<uint, Model>();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT id, model FROM doodad_almighties WHERE collide_ship = 1 AND no_collision = 0";
            using var reader = command.ExecuteReader();
            while (reader.Read())
                models.Add((uint)reader.GetInt64(0), new Model(reader.IsDBNull(1) ? "" : reader.GetString(1), []));
        }

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT id, doodad_almighty_id, model FROM doodad_func_groups WHERE model IS NOT NULL AND model <> ''";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                if (models.TryGetValue((uint)reader.GetInt64(1), out var model))
                    model.Phases.Add((uint)reader.GetInt64(0), reader.GetString(2));
            }
        }

        return new ShipDoodadModelCatalog(models);
    }

    /// <summary>A phase override belongs to its own template; absent overrides keep the template model.</summary>
    public bool TryGetModel(uint templateId, uint phase, out string? uri)
    {
        uri = null;
        if (!_models.TryGetValue(templateId, out var model))
            return false;
        uri = model.Phases.TryGetValue(phase, out var phaseModel) ? phaseModel : model.Uri;
        return true;
    }
}
