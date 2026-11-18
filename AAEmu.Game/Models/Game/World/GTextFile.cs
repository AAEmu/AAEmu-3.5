#nullable enable

using System.Globalization;
using System.Numerics;

namespace AAEmu.Game.Models.Game.World;

/// <summary>
/// One node of a level-design <c>.g</c> text file (<c>npc_spawners.g</c>, <c>spawn_point.g</c>,
/// <c>house.g</c>…): a keyword, an optional inline value and the indented children below it.
/// </summary>
/// <remarks>
/// The format is indentation based, four spaces per level:
/// <code>
/// spawner
///     spawnerId 484
///     points
///         point
///             pos ( x 1147.92, y 1665.42, z 359.134 )
///             zRot -2.89725
/// </code>
/// A value is everything after the keyword; a parenthesised tuple such as <c>( x 1, y 2 )</c> is kept
/// as text and decoded on demand with <see cref="TryReadVector3"/> / <see cref="ReadTuple"/>.
/// </remarks>
public sealed class GTextNode
{
    public GTextNode(string name, string value, int line)
    {
        Name = name;
        Value = value;
        Line = line;
    }

    public string Name { get; }
    /// <summary>Inline value, empty when the keyword stands alone.</summary>
    public string Value { get; }
    /// <summary>1-based source line, for diagnostics.</summary>
    public int Line { get; }
    public List<GTextNode> Children { get; } = [];

    public GTextNode? Child(string name)
    {
        foreach (var child in Children)
            if (string.Equals(child.Name, name, StringComparison.Ordinal))
                return child;
        return null;
    }

    public IEnumerable<GTextNode> ChildrenNamed(string name)
    {
        foreach (var child in Children)
            if (string.Equals(child.Name, name, StringComparison.Ordinal))
                yield return child;
    }

    public string? ChildValue(string name) => Child(name)?.Value;

    public bool TryGetUInt32(string name, out uint value)
    {
        value = 0;
        var text = ChildValue(name);
        return text != null && uint.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }

    public bool TryGetSingle(string name, out float value)
    {
        value = 0f;
        var text = ChildValue(name);
        return text != null && float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    /// <summary>Decodes <c>( x 1.5, y 2, z 3 )</c>; false when any of x, y, z is missing or malformed.</summary>
    public static bool TryReadVector3(string? tuple, out Vector3 value)
    {
        value = default;
        var fields = ReadTuple(tuple);
        if (!TryF(fields, "x", out var x) || !TryF(fields, "y", out var y) || !TryF(fields, "z", out var z))
            return false;
        value = new Vector3(x, y, z);
        return true;
    }

    /// <summary>Decodes <c>( key value, key value )</c> into its key/value pairs; empty for anything else.</summary>
    public static Dictionary<string, string> ReadTuple(string? tuple)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(tuple))
            return result;

        var open = tuple.IndexOf('(');
        var close = tuple.LastIndexOf(')');
        if (open < 0 || close <= open)
            return result;

        foreach (var part in tuple.AsSpan(open + 1, close - open - 1).ToString().Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var space = part.IndexOf(' ');
            if (space <= 0)
                continue;
            result[part[..space]] = part[(space + 1)..].Trim();
        }

        return result;
    }

    private static bool TryF(Dictionary<string, string> fields, string key, out float value)
    {
        value = 0f;
        return fields.TryGetValue(key, out var text) && float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }
}

/// <summary>Parser for the indentation-based <c>.g</c> level files. Returns the top-level nodes in file order.</summary>
public static class GTextFile
{
    public const int SpacesPerLevel = 4;

    public static List<GTextNode> ParseFile(string path) => Parse(File.ReadAllText(path));

    public static List<GTextNode> Parse(string text)
    {
        var roots = new List<GTextNode>();
        if (string.IsNullOrEmpty(text))
            return roots;

        // Index i holds the last node seen at depth i; a line at depth d attaches to stack[d-1].
        var stack = new List<GTextNode>();
        var lineNo = 0;
        foreach (var rawLine in text.Split('\n'))
        {
            lineNo++;
            var line = rawLine.TrimEnd('\r', ' ', '\t');
            if (line.Length == 0)
                continue;

            var indent = 0;
            var i = 0;
            while (i < line.Length && (line[i] == ' ' || line[i] == '\t'))
            {
                indent += line[i] == '\t' ? SpacesPerLevel : 1;
                i++;
            }

            var depth = indent / SpacesPerLevel;
            var content = line[i..];
            // Hand-written files sometimes brace a block ("spawner { … }"); the braces carry no
            // information of their own and must not reset the parent chain.
            if (content is "{" or "}")
                continue;

            // The keyword ends at the first space or at an opening parenthesis ("pos( x 1, … )").
            var cut = content.IndexOfAny([' ', '(']);
            var name = cut < 0 ? content : content[..cut];
            var value = cut < 0 ? string.Empty : content[cut..].Trim();
            var node = new GTextNode(name, value, lineNo);

            // A deeper jump than one level (sloppy indentation) attaches to the deepest open node.
            if (depth > stack.Count)
                depth = stack.Count;

            if (depth == 0)
                roots.Add(node);
            else
                stack[depth - 1].Children.Add(node);

            if (stack.Count > depth)
                stack.RemoveRange(depth, stack.Count - depth);
            stack.Add(node);
        }

        return roots;
    }
}
