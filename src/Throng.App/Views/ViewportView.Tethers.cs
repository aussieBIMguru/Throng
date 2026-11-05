using System.Numerics;
using System.Windows;
using Throng.Core.Geometry;
using Throng.Render.Gl;
using Throng.Sim.Engine;

namespace Throng.App.Views;

/// <summary>
/// Group tethers (round 024b, D-391): hovering a person who walks with a group draws thin lines to their mates; a
/// left click on one keeps their group tethered (cyan) until a click elsewhere. Works in plan (people on the active
/// level), 3D and ISO (every visible level), live and in replay. Mates are found by the group tag the snapshot carries
/// (float 11), so nothing here touches the run.
/// </summary>
public sealed partial class ViewportView
{
    /// <summary>How close the cursor must be to a person to pick them, DIPs (or their body, if bigger on screen).</summary>
    private const double TetherPickDip = 12;

    private readonly TetherLayer _tetherLayer = new();
    private readonly List<TetherLayer.Tether> _tethers = [];
    private Point? _tetherCursor;
    private float _pinnedTag;
    private Vector3 _pinnedAt;

    /// <summary>Puts the tether layer over the editor overlay (called once from the constructor).</summary>
    private void InitialiseTethers()
    {
        int at = Root.Children.IndexOf(Overlay);
        Root.Children.Insert(at < 0 ? Root.Children.Count : at + 1, _tetherLayer);
    }

    /// <summary>Remembers the cursor for the next frame's hover tether.</summary>
    private void TetherPointerMoved(Point position) => _tetherCursor = position;

    /// <summary>Forgets the cursor (it left the viewport).</summary>
    private void TetherPointerLeft() => _tetherCursor = null;

    /// <summary>
    /// A left click: picks the group of the person under the cursor (or drops the pick when there's none). The click
    /// still goes to the editor as usual.
    /// </summary>
    private void TetherClicked(Point position, int agents)
    {
        int i = agents > 0 ? PersonAt(position, agents) : -1;
        float tag = i >= 0 ? _agentBuffer[(i * SimEngine.FloatsPerAgent) + 11] : 0;
        if (tag >= 0.5f)
        {
            _pinnedTag = tag;
            _pinnedAt = AgentPosition(i);
        }
        else
        {
            _pinnedTag = 0;
        }
    }

    /// <summary>Rebuilds the tethers from this frame's people (call after the agent feed filled the buffer).</summary>
    private void UpdateTethers(int agents)
    {
        _tethers.Clear();
        if (agents <= 0)
        {
            _pinnedTag = 0;
            _tetherLayer.Set(_tethers);
            return;
        }

        const int F = SimEngine.FloatsPerAgent;
        int pinned = -1;
        if (_pinnedTag >= 0.5f)
        {
            // Follow the picked person: the member of the group nearest where they were last frame.
            float best = float.PositiveInfinity;
            for (int i = 0; i < agents; i++)
            {
                if (_agentBuffer[(i * F) + 11] == _pinnedTag)
                {
                    float d = Vector3.DistanceSquared(AgentPosition(i), _pinnedAt);
                    if (d < best)
                    {
                        best = d;
                        pinned = i;
                    }
                }
            }

            if (pinned >= 0)
            {
                _pinnedAt = AgentPosition(pinned);
                AddTether(pinned, agents, true);
            }
        }

        if (_tetherCursor is { } cursor && PersonAt(cursor, agents) is int hovered and >= 0
            && _agentBuffer[(hovered * F) + 11] >= 0.5f && _agentBuffer[(hovered * F) + 11] != _pinnedTag)
        {
            AddTether(hovered, agents, false);
        }

        _tetherLayer.Set(_tethers);
    }

    /// <summary>Adds the tether from person <paramref name="from"/> to every mate in view (none when they're alone in view).</summary>
    private void AddTether(int from, int agents, bool pinned)
    {
        const int F = SimEngine.FloatsPerAgent;
        float tag = _agentBuffer[(from * F) + 11];
        if (ScreenOf(from) is not { } start)
        {
            return;
        }

        var mates = new List<Point>();
        for (int i = 0; i < agents; i++)
        {
            if (i != from && _agentBuffer[(i * F) + 11] == tag && ScreenOf(i) is { } mate)
            {
                mates.Add(mate);
            }
        }

        if (mates.Count > 0)
        {
            _tethers.Add(new TetherLayer.Tether(start, mates, mates.Count + 1, pinned));
        }
    }

    /// <summary>The person nearest <paramref name="position"/> on screen within the pick distance, or −1.</summary>
    private int PersonAt(Point position, int agents)
    {
        const int F = SimEngine.FloatsPerAgent;
        int best = -1;
        double bestD = double.PositiveInfinity;
        double dipsPerMetre = _mode == ViewMode.Plan ? 1 / Math.Max(1e-6, _planCamera.MetresPerDip) : 0;
        for (int i = 0; i < agents; i++)
        {
            if (ScreenOf(i) is not { } p)
            {
                continue;
            }

            double reach = Math.Max(TetherPickDip, _agentBuffer[(i * F) + 7] * dipsPerMetre);
            double dx = p.X - position.X, dy = p.Y - position.Y, d = (dx * dx) + (dy * dy);
            if (d <= reach * reach && d < bestD)
            {
                bestD = d;
                best = i;
            }
        }

        return best;
    }

    /// <summary>Person <paramref name="i"/>'s true position (x, y, floor height).</summary>
    private Vector3 AgentPosition(int i)
    {
        int o = i * SimEngine.FloatsPerAgent;
        return new Vector3(_agentBuffer[o], _agentBuffer[o + 1], _agentBuffer[o + 2]);
    }

    /// <summary>Person <paramref name="i"/> on screen: their plan point in plan, their chest in 3D / ISO; null when behind the camera.</summary>
    private Point? ScreenOf(int i)
    {
        int o = i * SimEngine.FloatsPerAgent;
        float x = _agentBuffer[o], y = _agentBuffer[o + 1];
        if (_mode == ViewMode.Plan)
        {
            return PlanToScreen(new Point2(x, y));
        }

        // Exploded views draw each level at its warped height (D-263); the chest sits at about 60 % of the body.
        double z = _renderer.WarpZ(_agentBuffer[o + 2]) + (0.6 * _agentBuffer[o + 8]);
        return ToScreen(new Vector3(x, y, (float)z));
    }
}
