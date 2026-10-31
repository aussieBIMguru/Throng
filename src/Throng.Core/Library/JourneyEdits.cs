using System.Globalization;

namespace Throng.Core.Library;

/// <summary>
/// Edits on a journey graph (D-325–D-327), for the JOURNEY canvas and SELECTION. Each works on the journey it's given
/// (the editor passes a copy and puts the result through the undo stack) and keeps the graph sound: wires never go
/// into Spawn, from a port the node doesn't have, or round a loop, and an output holds at most one wire.
/// </summary>
public static class JourneyEdits
{
    /// <summary>A new node of a kind with sensible defaults, at a canvas point.</summary>
    /// <param name="kind">Node kind.</param>
    /// <param name="x">Canvas x of the top-left corner.</param>
    /// <param name="y">Canvas y of the top-left corner.</param>
    /// <param name="deviceType">Type tag for a new device node (e.g. the study's first device type).</param>
    public static JourneyNode Create(JourneyNodeKind kind, double x, double y, string deviceType = "Gate") => kind switch
    {
        JourneyNodeKind.Device => new JourneyNode { Kind = kind, TargetTag = deviceType, X = x, Y = y },
        JourneyNodeKind.Dwell => new JourneyNode { Kind = kind, DwellS = JourneyGraph.DefaultDwell, X = x, Y = y },
        JourneyNodeKind.Chance => new JourneyNode { Kind = kind, Weights = [1, 1], X = x, Y = y },
        JourneyNodeKind.TagCheck or JourneyNodeKind.SetTag => new JourneyNode { Kind = kind, TargetTag = "tag", X = x, Y = y },
        JourneyNodeKind.Sink => new JourneyNode { Kind = kind, SinkKind = JourneySinkKind.Destinations, X = x, Y = y },
        _ => new JourneyNode { Kind = kind, X = x, Y = y },
    };

    /// <summary>Adds a node (never a second Spawn) and returns its id, or null when refused.</summary>
    public static Guid? Add(JourneyTemplate journey, JourneyNode node)
    {
        if (node.Kind == JourneyNodeKind.Spawn && JourneyGraph.Spawn(journey) is not null)
        {
            return null;
        }

        JourneyNode added = journey.Nodes.Any(n => n.Id == node.Id) ? node with { Id = Guid.NewGuid() } : node;
        journey.Nodes = [.. journey.Nodes, added];
        return added.Id;
    }

    /// <summary>Moves a node; false when there's no such node or it didn't move.</summary>
    public static bool Move(JourneyTemplate journey, Guid id, double x, double y)
    {
        int k = journey.Nodes.FindIndex(n => n.Id == id);
        if (k < 0 || !double.IsFinite(x) || !double.IsFinite(y) || (journey.Nodes[k].X == x && journey.Nodes[k].Y == y))
        {
            return false;
        }

        List<JourneyNode> nodes = [.. journey.Nodes];
        nodes[k] = nodes[k] with { X = Math.Round(x, 1), Y = Math.Round(y, 1) };
        journey.Nodes = nodes;
        return true;
    }

    /// <summary>
    /// Wires output <paramref name="port"/> of <paramref name="from"/> into <paramref name="to"/>, replacing any wire
    /// already on that output (D-327). Returns null when done, else why it was refused.
    /// </summary>
    public static string? Connect(JourneyTemplate journey, Guid from, int port, Guid to)
    {
        if (JourneyGraph.Find(journey, from) is not { } source || JourneyGraph.Find(journey, to) is not { } target)
        {
            return "That node is gone.";
        }

        if (port < 0 || port >= JourneyGraph.OutputCount(source))
        {
            return $"'{source.Title}' has no such output.";
        }

        if (target.Kind == JourneyNodeKind.Spawn)
        {
            return "Nothing wires into Spawn: it's where people start.";
        }

        if (from == to)
        {
            return "A node can't wire into itself (no loops, D-306).";
        }

        List<JourneyWire> before = journey.Wires;
        List<JourneyWire> next = [.. before.Where(w => !(w.From == from && w.Port == port)), JourneyWire.Of(from, port, to)];
        journey.Wires = next;
        if (JourneyGraph.HasLoop(journey))
        {
            journey.Wires = before;
            return $"Wiring '{source.Title}' into '{target.Title}' would make a loop; journeys run forward from Spawn to a Sink (D-306).";
        }

        return null;
    }

    /// <summary>Removes the wire on an output; false when there was none.</summary>
    public static bool Disconnect(JourneyTemplate journey, Guid from, int port)
    {
        int count = journey.Wires.Count;
        journey.Wires = [.. journey.Wires.Where(w => !(w.From == from && w.Port == port))];
        return journey.Wires.Count != count;
    }

    /// <summary>Removes a node (never Spawn) and every wire to or from it; false when refused.</summary>
    public static bool Remove(JourneyTemplate journey, Guid id)
    {
        if (JourneyGraph.Find(journey, id) is not { } node || node.Kind == JourneyNodeKind.Spawn)
        {
            return false;
        }

        journey.Nodes = [.. journey.Nodes.Where(n => n.Id != id)];
        journey.Wires = [.. journey.Wires.Where(w => w.From != id && w.To != id)];
        return true;
    }

    /// <summary>
    /// Replaces a node by id (its settings changed). Wires from outputs it no longer has are dropped (a Chance with
    /// fewer weights, a Sink). False when there's no such node or nothing changed.
    /// </summary>
    public static bool Replace(JourneyTemplate journey, JourneyNode node)
    {
        int k = journey.Nodes.FindIndex(n => n.Id == node.Id);
        if (k < 0 || Equals(journey.Nodes[k], node))
        {
            return false;
        }

        // A second Spawn by changing kind isn't allowed; the kind of Spawn itself never changes.
        if ((node.Kind == JourneyNodeKind.Spawn) != (journey.Nodes[k].Kind == JourneyNodeKind.Spawn))
        {
            return false;
        }

        List<JourneyNode> nodes = [.. journey.Nodes];
        nodes[k] = node;
        journey.Nodes = nodes;
        int outputs = JourneyGraph.OutputCount(node);
        journey.Wires = [.. journey.Wires.Where(w => w.From != node.Id || w.Port < outputs)];
        return true;
    }

    /// <summary>A Chance node's weights set from text like "3, 1, 1" (2–6 values, each zero or more).</summary>
    public static bool TryParseWeights(string? text, out List<double> weights)
    {
        weights = [];
        foreach (string part in (text ?? string.Empty).Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries))
        {
            if (!double.TryParse(part, NumberStyles.Float, CultureInfo.InvariantCulture, out double w) || !double.IsFinite(w) || w < 0)
            {
                return false;
            }

            weights.Add(w);
        }

        return weights.Count is >= 2 and <= JourneyNode.MaxChanceOutputs && weights.Any(w => w > 0);
    }

    /// <summary>A dwell range from text: "60" (fixed) or "30-180" (uniform), seconds, 0–86 400.</summary>
    public static bool TryParseRange(string? text, out Distribution? range)
    {
        range = null;
        string t = (text ?? string.Empty).Replace('–', '-').Trim();
        string[] parts = t.Split('-', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var values = new List<double>();
        foreach (string part in parts)
        {
            if (!double.TryParse(part, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) || !double.IsFinite(v) || v < 0 || v > 86400)
            {
                return false;
            }

            values.Add(v);
        }

        range = values.Count switch
        {
            1 => Distribution.Fixed(values[0]),
            2 => Distribution.Uniform(Math.Min(values[0], values[1]), Math.Max(values[0], values[1])),
            _ => null,
        };
        return range is not null;
    }

    /// <summary>
    /// Toggles a device or zone in a node's pick list (D-318, D-324): added with weight 1, or removed. Returns the
    /// updated node.
    /// </summary>
    public static JourneyNode TogglePick(JourneyNode node, Guid id)
    {
        List<JourneyPick> picks = [.. node.Picks ?? []];
        int k = picks.FindIndex(p => p.Id == id);
        if (k >= 0)
        {
            picks.RemoveAt(k);
        }
        else
        {
            picks.Add(JourneyPick.Of(id));
        }

        return node with { Picks = picks.Count > 0 ? picks : null };
    }
}
