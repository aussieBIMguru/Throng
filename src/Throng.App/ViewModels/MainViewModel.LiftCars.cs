using Throng.Core.Geometry;
using Throng.Render.Scene;
using Throng.Sim;
using Throng.Sim.Engine;

namespace Throng.App.ViewModels;

/// <summary>
/// The 3D lift cars while running (round 013b, D-240, D-245): a box per car at its current height in its shaft, with
/// its doors marked on the sides that open at the stop it's at, lit while open. The riders themselves come with the
/// agent snapshot (already carried at the car's height, 013a).
/// </summary>
public sealed partial class MainViewModel
{
    /// <summary>Car inside height, metres (lower when the storey above is closer).</summary>
    private const double CarHeightM = 2.4;

    private IReadOnlyList<LiftCarDraw>? _liftCarDraws;
    private double[] _liftCarSignature = [];

    /// <summary>
    /// The cars to draw in 3D this frame, or null with no run or no lifts. The same list is returned while no car
    /// moved or changed state, so the renderer only rebuilds meshes when something changed.
    /// </summary>
    public IReadOnlyList<LiftCarDraw>? FetchLiftCars3D()
    {
        if (_runner is not { } runner || runner.Model.Lifts.Count == 0)
        {
            _liftCarDraws = null;
            _liftCarSignature = [];
            return null;
        }

        IReadOnlyList<(SimLift Lift, int Car, double Z, LiftCarStatus State, int Riders, int Direction)> states = runner.CopyLiftCarStates();
        if (_liftCarDraws is not null && SameSignature(states))
        {
            return _liftCarDraws;
        }

        var signature = new double[states.Count * 2];
        var draws = new List<LiftCarDraw>(states.Count);
        for (int k = 0; k < states.Count; k++)
        {
            (SimLift lift, int car, double z, LiftCarStatus state, _, _) = states[k];
            signature[k * 2] = z;
            signature[(k * 2) + 1] = (int)state;
            if (car >= lift.CarLayouts.Count)
            {
                continue;
            }

            LiftCarLayout layout = lift.CarLayouts[car];
            int stop = StopAt(lift, z);
            var doors = new List<(Point2 A, Point2 B)>(2);
            for (int side = 0; side < 2; side++)
            {
                // At a stop: the sides with a landing there; between stops: every door the car has.
                bool used = stop >= 0 ? lift.PortalAt(stop, side) >= 0 : layout.Door(side) is not null;
                if (used && layout.Door(side) is { } door)
                {
                    doors.Add(door);
                }
            }

            bool open = stop >= 0 && state is LiftCarStatus.Opening or LiftCarStatus.Open;
            draws.Add(new LiftCarDraw(layout.Footprint, z, HeightFor(lift), doors, open));
        }

        _liftCarSignature = signature;
        _liftCarDraws = draws;
        return draws;
    }

    /// <summary>True when every car's height and state match the last list built.</summary>
    private bool SameSignature(IReadOnlyList<(SimLift Lift, int Car, double Z, LiftCarStatus State, int Riders, int Direction)> states)
    {
        if (_liftCarSignature.Length != states.Count * 2)
        {
            return false;
        }

        for (int k = 0; k < states.Count; k++)
        {
            if (Math.Abs(_liftCarSignature[k * 2] - states[k].Z) > 1e-4 || (int)_liftCarSignature[(k * 2) + 1] != (int)states[k].State)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>The stop a car is standing at, or −1 between stops.</summary>
    private static int StopAt(SimLift lift, double z)
    {
        for (int s = 0; s < lift.Stops; s++)
        {
            if (Math.Abs(lift.StopZ[s] - z) < 0.05)
            {
                return s;
            }
        }

        return -1;
    }

    /// <summary>The car height: <see cref="CarHeightM"/>, or less where the stops are closer than a storey.</summary>
    private static double HeightFor(SimLift lift)
    {
        double gap = double.PositiveInfinity;
        for (int s = 1; s < lift.Stops; s++)
        {
            gap = Math.Min(gap, lift.StopZ[s] - lift.StopZ[s - 1]);
        }

        return double.IsFinite(gap) ? Math.Clamp(gap - 0.4, 1.8, CarHeightM) : CarHeightM;
    }
}
