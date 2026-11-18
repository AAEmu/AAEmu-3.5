using System.Numerics;
using AAEmu.Game.Models.CryEngine.Readers;

namespace AAEmu.Game.Core.Managers.Bots.Navigation;

/// <summary>
/// A corner of a block's triangulation, in world coordinates: an obstacle point (rock, wall corner, tree; the file's
/// flag 1) or, when <see cref="Obstacle"/> is false, a plain point such as a block corner.
/// </summary>
public readonly record struct NavRawVertex(Vector3 Position, float Radius, bool Obstacle = true);

/// <summary>
/// A navigation graph node as the client file has it. Type 2 is a triangle with corners <see cref="A"/>,
/// <see cref="B"/> and <see cref="C"/> (indices into the block's vertices) and <see cref="Position"/> inside it on
/// the ground; <see cref="Flags"/> is the file's first bit field (48 marks a triangle inside a blocked area such as
/// a building's footprint). Type 4 is a waypoint of a structure's grid, walked only with
/// <c>Navigation.UseStructureGrids</c>; other types are not walked.
/// </summary>
public readonly record struct NavRawNode(int Id, byte Type, byte Flags, int A, int B, int C, Vector3 Position);

/// <summary>
/// A graph link as the client file has it: from node <see cref="From"/> into node <see cref="To"/> across the edge
/// between corners <see cref="StartIndex"/> and <see cref="EndIndex"/> (0..2) of <see cref="From"/>.
/// <see cref="MaxPassRadius"/> is the widest walker that fits through; below 0 the link is blocked (one way: the
/// file blocks the way into a blocked area but not the way out).
/// </summary>
public readonly record struct NavRawLink(int From, int To, float MaxPassRadius, Vector3 EdgeCenter, byte StartIndex, byte EndIndex);

/// <summary>
/// The raw content of one 256 m mesh block (<c>paths/XXX_YYY/netmission0.bai</c> and <c>vertsmission0.bai</c>),
/// in world coordinates, before <see cref="NavBlock"/> keeps what path-finding uses. Tests build these by hand.
/// </summary>
public sealed class NavBlockData
{
    public List<NavRawVertex> Vertices { get; } = [];
    public List<NavRawNode> Nodes { get; } = [];
    public List<NavRawLink> Links { get; } = [];

    /// <summary>
    /// Copies what the server's own readers parsed. Nodes are put in id order, so a block is built the same way
    /// whatever order the reader's dictionary hands them out in.
    /// </summary>
    public static NavBlockData FromReaders(NetMissionReader net, VertexMissionReader vertices)
    {
        var data = new NavBlockData();
        if (vertices != null)
        {
            foreach (var v in vertices.ObstacleDataDescriptorList)
                data.Vertices.Add(new NavRawVertex(v.Pos, (float)v.ApproxRadius, (v.Flags & 1) != 0));
        }

        if (net == null)
            return data;

        foreach (var node in net.NodeDescriptorList.Values.OrderBy(n => n.Id))
        {
            var corners = node.Obstacle ?? [];
            data.Nodes.Add(new NavRawNode(node.Id, node.Type, node.BitField0,
                corners.Length > 0 ? corners[0] : -1,
                corners.Length > 1 ? corners[1] : -1,
                corners.Length > 2 ? corners[2] : -1,
                node.Pos));
        }

        foreach (var link in net.LinkDescriptorList)
        {
            if (link.SourceNode is < int.MinValue or > int.MaxValue || link.TargetNode is < int.MinValue or > int.MaxValue)
                continue;
            data.Links.Add(new NavRawLink((int)link.SourceNode, (int)link.TargetNode, (float)link.MaxPassRadius,
                link.EdgeCenter, link.StartIndex, link.EndIndex));
        }

        return data;
    }
}
