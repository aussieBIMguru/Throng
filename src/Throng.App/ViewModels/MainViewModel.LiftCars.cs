using Throng.Core.Geometry;
using Throng.Core.Model;
using Throng.Render.Scene;
using Throng.Sim;
using Throng.Sim.Engine;

namespace Throng.App.ViewModels;

/// <summary>
/// The 3D lift cars while running (round 013b, D-240, D-245): a box per car at its current height in its shaft, with
/// its door openings framed on the sides that open at the stop it's at, lit while open; and (round 014, D-258)
/// centre-opening sliding leaves on every car door and every landing door, timed by the preset's open/close times.
/// Cars and landings on hidden levels aren't drawn (D-252). The riders come with the agent snapshot.
/// </summary>
public sealed partial class MainViewModel
{
    /// <summary>Plan gap between a door's leaves and its opening line (car leaves inside, landing leaves outside), m.</summary>
    private const double LeafOffsetM = 0.05;

    /// <summary>Values per car in the change signature: height, state, shown.</summary>
    private const int SignatureStride = 3;

    private LiftSceneDraw? _liftScene;
    private double[] _liftCarSignature = [];
    private bool[] _liftLevelShown = [];

    /// <summary>
    /// The cars and doors to draw in 3D this frame, or null with no run or no lifts. The same instance is returned while
    /// no car moved, changed state or was shown/hidden and no door is sliding, so the renderer only rebuilds meshes when
    /// something changed.
    /// </summary>
    public LiftSceneDraw? FetchLiftCars3D()
    {
        if (_runner is not { } runner || runner.Model.Lifts.Count == 0)
        {
            _liftScene = null;
            _liftCarSignature = [];
            return null;
        }

        IReadOnlyList<(SimLift Lift, int Car, double Z, LiftCarStatus State, int Riders, int Direction)> states = runner.CopyLiftCarStates();
        IReadOnlyList<Level> levels = _study.LevelsAscending();
        bool[] levelShown = new bool[levels.Count];
        for (int k = 0; k < levels.Count; k++)
        {
            levelShown[k] = levels[k].IsVisible;
        }

        if (_liftScene is not null && SameSignature(states, levels) && levelShown.AsSpan().SequenceEqual(_liftLevelShown))
        {
            return _liftScene;
        }

        double[] shares = runner.CopyLiftDoorShares();
        var signature = new double[states.Count * SignatureStride];
        var cars = new List<LiftCarDraw>(states.Count);
        var doors = new List<LiftDoorDraw>();
        for (int k = 0; k < states.Count; k++)
        {
            (SimLift lift, int car, double z, LiftCarStatus state, _, _) = states[k];
            bool shown = IsHeightShown(levels, z);
            signature[k * SignatureStride] = z;
            signature[(k * SignatureStride) + 1] = (int)state;
            signature[(k * SignatureStride) + 2] = shown ? 1 : 0;
            if (car >= lift.CarLayouts.Count || !shown)
            {
                continue;
            }

            LiftCarLayout layout = lift.CarLayouts[car];
            int stop = StopAt(lift, z);
            double share = stop >= 0 && lift.FirstCar + car < shares.Length ? shares[lift.FirstCar + car] : 0;
            double height = HeightFor(lift);
            var openings = new List<(Point2 A, Point2 B)>(2);
            for (int side = 0; side < 2; side++)
            {
                // At a stop: the sides with a landing there; between stops: every door the car has.
                bool used = stop >= 0 ? lift.PortalAt(stop, side) >= 0 : layout.Door(side) is not null;
                if (used && layout.Door(side) is { } door)
                {
                    openings.Add(door);
                    Point2 inward = lift.Sides[side] is { } liftSide ? liftSide.Outward * -LeafOffsetM : new Point2(0, 0);
                    doors.Add(new LiftDoorDraw(door.A, door.B, inward, z, height * LiftCarMeshes.DoorHeightShare, share, IsLanding: false));
                }
            }

            cars.Add(new LiftCarDraw(layout.Footprint, z, height, openings, share));
        }

        AddLandingDoors(runner.Model.Lifts, states, shares, levels, levelShown, doors);
        _liftCarSignature = signature;
        _liftLevelShown = levelShown;
        _liftScene = new LiftSceneDraw(cars, doors);
        return _liftScene;
    }

    /// <summary>
    /// A landing door in the shaft wall in front of every car door at every stop and side with a landing, on shown
    /// levels; open only where that car stands at that stop (D-258).
    /// </summary>
    private static void AddLandingDoors(
        IReadOnlyList<SimLift> lifts,
        IReadOnlyList<(SimLift Lift, int Car, double Z, LiftCarStatus State, int Riders, int Direction)> states,
        double[] shares,
        IReadOnlyList<Level> levels,
        bool[] levelShown,
        List<LiftDoorDraw> doors)
    {
        foreach (SimLift lift in lifts)
        {
            double height = HeightFor(lift) * LiftCarMeshes.DoorHeightShare;
            for (int stop = 0; stop < lift.Stops; stop++)
            {
                int levelIndex = IndexOfLevel(levels, lift.StopLevels[stop]);
                if (levelIndex >= 0 && !levelShown[levelIndex])
                {
                    continue;
                }

                for (int side = 0; side < 2; side++)
                {
                    if (lift.PortalAt(stop, side) < 0 || lift.Sides[side] is not { } liftSide)
                    {
                        continue;
                    }

                    for (int c = 0; c < lift.Cars; c++)
                    {
                        if (lift.CarLayouts[c].Door(side) is not { } door)
                        {
                            continue;
                        }

                        double share = 0;
                        foreach ((SimLift l, int car, double z, _, _, _) in states)
                        {
                            if (ReferenceEquals(l, lift) && car == c && StopAt(lift, z) == stop && lift.FirstCar + c < shares.Length)
                            {
                                share = shares[lift.FirstCar + c];
                                break;
                            }
                        }

                        doors.Add(new LiftDoorDraw(door.A, door.B, liftSide.Outward * LeafOffsetM, lift.StopZ[stop], height, share, IsLanding: true));
                    }
                }
            }
        }
    }

    /// <summary>Index of a level in the list, or −1.</summary>
    private static int IndexOfLevel(IReadOnlyList<Level> levels, Guid id)
    {
        for (int k = 0; k < levels.Count; k++)
        {
            if (levels[k].Id == id)
            {
                return k;
            }
        }

        return -1;
    }

    /// <summary>
    /// True when every car's height, state and visibility match the last scene built, and no door is mid-slide.
    /// </summary>
    private bool SameSignature(
        IReadOnlyList<(SimLift Lift, int Car, double Z, LiftCarStatus State, int Riders, int Direction)> states, IReadOnlyList<Level> levels)
    {
        if (_liftCarSignature.Length != states.Count * SignatureStride)
        {
            return false;
        }

        for (int k = 0; k < states.Count; k++)
        {
            int o = k * SignatureStride;
            LiftCarStatus state = states[k].State;
            if (state is LiftCarStatus.Opening or LiftCarStatus.Closing
                || Math.Abs(_liftCarSignature[o] - states[k].Z) > 1e-4
                || (int)_liftCarSignature[o + 1] != (int)state
                || (_liftCarSignature[o + 2] > 0) != IsHeightShown(levels, states[k].Z))
            {
                // Sliding doors (D-258) change every frame while opening or closing.
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// True when a car floor at this height is on a level shown in 3D (the same height bands the riders are filtered by,
    /// so a car hides with its level, D-252).
    /// </summary>
    private static bool IsHeightShown(IReadOnlyList<Level> levels, double z)
    {
        for (int k = 0; k < levels.Count; k++)
        {
            double lo = k == 0 ? double.NegativeInfinity : levels[k].ElevationM - 0.5;
            double hi = k == levels.Count - 1 ? double.PositiveInfinity : levels[k + 1].ElevationM - 0.5;
            if (z >= lo && z < hi)
            {
                return levels[k].IsVisible;
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

    /// <summary>The car height: 2.4 m, or less where the stops are closer than a storey (shared with the shaft walls, D-261).</summary>
    private static double HeightFor(SimLift lift) => LiftLayout.CarHeightFor(lift.StopZ);
}
