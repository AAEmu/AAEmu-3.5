using AAEmu.Commons.Utils;
using AAEmu.Game.GameData.Framework;

using Microsoft.Data.Sqlite;

namespace AAEmu.Game.GameData;

[GameData]
public sealed class FormPassiveGameData : Singleton<FormPassiveGameData>, IGameDataLoader
{
    private Dictionary<uint, HashSet<uint>> _passivesByForm = [];

    internal IReadOnlySet<uint> GetGrantedPassives(uint formBuffId) =>
        Volatile.Read(ref _passivesByForm).GetValueOrDefault(formBuffId);

    public void Load(SqliteConnection connection, SqliteConnection connection2)
    {
        var forms = new Dictionary<uint, HashSet<uint>>();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT buff_id, passive_buff_id FROM buff_passive_buffs WHERE buff_id > 0 AND passive_buff_id > 0";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var form = checked((uint)reader.GetInt64(0));
            var passive = checked((uint)reader.GetInt64(1));
            if (!forms.TryGetValue(form, out var passives))
                forms.Add(form, passives = []);
            passives.Add(passive);
        }

        Volatile.Write(ref _passivesByForm, forms);
    }

    public void PostLoad()
    {
    }
}
