namespace AAEmu.Geometry;

public enum GeometryLogLevel
{
    Debug,
    Info,
    Warn,
    Error
}

/// <summary>
/// Where the geometry package writes its diagnostics. The package has no logging dependency of its own
/// (NLog would tie it to the game trees), so a host assigns <see cref="Sink"/> once at startup, e.g. to
/// forward into NLog. Unassigned means silent, which keeps tests and the probe quiet.
/// </summary>
public static class GeometryLog
{
    /// <summary><c>(level, message, exception)</c>; exception is null when there is none.</summary>
    public static Action<GeometryLogLevel, string, Exception?>? Sink { get; set; }

    public static void Debug(string message) => Sink?.Invoke(GeometryLogLevel.Debug, message, null);

    public static void Info(string message) => Sink?.Invoke(GeometryLogLevel.Info, message, null);

    public static void Warn(string message, Exception? exception = null) =>
        Sink?.Invoke(GeometryLogLevel.Warn, message, exception);

    public static void Error(string message, Exception? exception = null) =>
        Sink?.Invoke(GeometryLogLevel.Error, message, exception);
}
