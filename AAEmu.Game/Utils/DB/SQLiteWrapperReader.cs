using Microsoft.Data.Sqlite;

namespace AAEmu.Game.Utils.DB;

public sealed class SQLiteWrapperReader(SqliteDataReader reader, bool nullsAsDefaults = false) : IDisposable
{
    private readonly Dictionary<string, int> _ordinal = [];
    private readonly bool _nullsAsDefaults = nullsAsDefaults;

    public bool Read() => reader.Read();

    public object GetValue(string column)
    {
        return reader.GetValue(GetOrdinal(column));
    }

    public bool GetBoolean(string column)
    {
        var ordinal = GetOrdinal(column);
        return IsNullDefault(ordinal) ? false : reader.GetBoolean(ordinal);
    }

    public bool GetBoolean(string column, bool fromString)
    {
        if (fromString)
        {
            if (IsDBNull(column))
                return false;

            var value = GetString(column);
            return value == "t" || value == "1";
        }

        return GetBoolean(column);
    }

    public byte GetByte(string column)
    {
        var ordinal = GetOrdinal(column);
        return IsNullDefault(ordinal) ? (byte)0 : reader.GetByte(ordinal);
    }

    public byte GetByte(string column, byte defaultValue)
    {
        var ordinal = GetOrdinal(column);
        if (reader.IsDBNull(ordinal))
            return defaultValue;
        return reader.GetByte(ordinal);
    }

    public long GetBytes(string column, long fieldOffset, byte[] buffer, int bufferOffset, int length)
    {
        return reader.GetBytes(GetOrdinal(column), fieldOffset, buffer, bufferOffset, length);
    }

    public char GetChar(string column)
    {
        var ordinal = GetOrdinal(column);
        return IsNullDefault(ordinal) ? '\0' : reader.GetChar(ordinal);
    }

    public long GetChars(string column, long fieldOffset, char[] buffer, int bufferOffset, int length)
    {
        return reader.GetChars(GetOrdinal(column), fieldOffset, buffer, bufferOffset, length);
    }

    public Guid GetGuid(string column)
    {
        var ordinal = GetOrdinal(column);
        return IsNullDefault(ordinal) ? Guid.Empty : reader.GetGuid(ordinal);
    }

    public short GetInt16(string column)
    {
        var ordinal = GetOrdinal(column);
        return IsNullDefault(ordinal) ? (short)0 : reader.GetInt16(ordinal);
    }

    public ushort GetUInt16(string column) => (ushort)GetInt16(column);

    public int GetInt32(string column)
    {
        var ordinal = GetOrdinal(column);
        if (IsNullDefault(ordinal))
            return 0;

        //Same impl of Sqlite.Core v2.2.1
        return (int)reader.GetInt64(ordinal);
    }

    public int GetInt32(string column, int defaultValue)
    {
        var ordinal = GetOrdinal(column);
        if (reader.IsDBNull(ordinal))
            return defaultValue;

        //Same impl of Sqlite.Core v2.2.1
        return (int)reader.GetInt64(ordinal);
    }

    public uint GetUInt32(string column) => (uint)GetInt32(column);

    public uint GetUInt32(string column, uint defaultValue)
    {
        var ordinal = GetOrdinal(column);
        if (reader.IsDBNull(ordinal))
            return defaultValue;
        return (uint)GetInt32(column);
    }

    public long GetInt64(string column)
    {
        var ordinal = GetOrdinal(column);
        return IsNullDefault(ordinal) ? 0L : reader.GetInt64(ordinal);
    }

    public ulong GetUInt64(string column) => (ulong)GetInt64(column);

    public float GetFloat(string column)
    {
        var ordinal = GetOrdinal(column);
        return IsNullDefault(ordinal) ? 0f : reader.GetFloat(ordinal);
    }

    public float GetFloat(string column, float defaultValue)
    {
        var ordinal = GetOrdinal(column);
        if (reader.IsDBNull(ordinal))
            return defaultValue;
        return reader.GetFloat(ordinal);
    }

    public double GetDouble(string column)
    {
        var ordinal = GetOrdinal(column);
        return IsNullDefault(ordinal) ? 0d : reader.GetDouble(ordinal);
    }

    public string GetString(string column)
    {
        var ordinal = GetOrdinal(column);
        return IsNullDefault(ordinal) ? string.Empty : reader.GetString(ordinal);
    }

    public string GetString(string column, string defaultValue)
    {
        var ordinal = GetOrdinal(column);
        if (reader.IsDBNull(ordinal))
            return defaultValue;
        return reader.GetString(ordinal);
    }

    public decimal GetDecimal(string column)
    {
        var ordinal = GetOrdinal(column);
        return IsNullDefault(ordinal) ? 0m : reader.GetDecimal(ordinal);
    }

    public DateTime GetDateTime(string column)
    {
        var ordinal = GetOrdinal(column);
        return IsNullDefault(ordinal) ? default : reader.GetDateTime(ordinal);
    }

    public bool IsDBNull(string column)
    {
        return reader.IsDBNull(GetOrdinal(column));
    }

    public int GetOrdinal(string column)
    {
        if (_ordinal.TryGetValue(column, out var ordinal1))
            return ordinal1;

        var ordinal = reader.GetOrdinal(column);
        _ordinal.Add(column, ordinal);
        return ordinal;
    }

    private bool IsNullDefault(int ordinal)
    {
        return _nullsAsDefaults && reader.IsDBNull(ordinal);
    }

    public void Dispose()
    {
        _ordinal.Clear();
        reader.Dispose();
    }
}
