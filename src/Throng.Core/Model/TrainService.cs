using System.Globalization;
using Throng.Core.Geometry;

namespace Throng.Core.Model;

/// <summary>
/// A train service (D-184, D-185): a group of door zones on a platform edge driven by a timetable. Every
/// <see cref="HeadwayS"/> from <see cref="OffsetS"/> a train arrives, people get off through the doors over
/// <see cref="AlightOverS"/>, then people waiting board (alight first, then board, D-186) until
/// <see cref="Capacity"/> is used up (D-187), and the doors close <see cref="DwellS"/> after arrival.
/// </summary>
/// <remarks>
/// Who gets off comes from the door zones' own emission rules (their mix lines and destinations, with their rates as
/// relative shares); a door zone owned by a service doesn't emit continuously. Immutable: edits replace the record in
/// <see cref="Study.TrainServices"/>, so undo restores the exact value.
/// </remarks>
public sealed record TrainService
{
    /// <summary>Default time between trains, seconds.</summary>
    public const double DefaultHeadwayS = 360;

    /// <summary>Default doors-open time, seconds.</summary>
    public const double DefaultDwellS = 45;

    /// <summary>Stable id.</summary>
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>Display name, e.g. "Southbound".</summary>
    public string Name { get; init; } = "Train service";

    /// <summary>
    /// Tag a journey's Board train step asks for (case-insensitive); empty = the step can only pick this service when
    /// it asks for any service.
    /// </summary>
    public string Tag { get; init; } = string.Empty;

    /// <summary>The door zones (spawn/sink zones on one level), in platform order.</summary>
    public List<Guid> DoorZoneIds { get; init; } = [];

    /// <summary>
    /// The train's drawn outline on the doors' level (D-249), in plan metres; empty = placed automatically as a box
    /// along the platform edge. Drawing only: the door zones stay the doors. Additive within format v9.
    /// </summary>
    public List<Point2> Shape { get; init; } = [];

    /// <summary>Time between trains, seconds.</summary>
    public double HeadwayS { get; init; } = DefaultHeadwayS;

    /// <summary>Arrival time of the first train after the run starts, seconds.</summary>
    public double OffsetS { get; init; } = 30;

    /// <summary>How long the doors stay open from arrival, seconds.</summary>
    public double DwellS { get; init; } = DefaultDwellS;

    /// <summary>People getting off each train (spread over the doors).</summary>
    public int AlightPerTrain { get; init; } = 150;

    /// <summary>How long getting off takes, seconds (boarding starts once it eases, D-186).</summary>
    public double AlightOverS { get; init; } = 20;

    /// <summary>Boarding places per train (D-187); people who don't fit wait for the next one.</summary>
    public int Capacity { get; init; } = 300;

    /// <summary>
    /// Optional load curve over the run (D-185): multiplies <see cref="AlightPerTrain"/> by the curve's value at each
    /// arrival (same points as emission rate curves). Empty = flat.
    /// </summary>
    public List<RateCurvePoint> RateCurve { get; init; } = [];

    /// <summary>
    /// The services a Board train step asking for <paramref name="tag"/> may use (D-186): every service for an empty
    /// tag, else those whose tag or name matches (case-insensitive).
    /// </summary>
    public static IReadOnlyList<TrainService> Matching(Study study, string? tag)
    {
        string wanted = tag?.Trim() ?? string.Empty;
        return [.. study.TrainServices.Where(t => t is not null && (wanted.Length == 0
            || string.Equals(t.Tag?.Trim(), wanted, StringComparison.OrdinalIgnoreCase)
            || string.Equals(t.Name?.Trim(), wanted, StringComparison.OrdinalIgnoreCase)))];
    }

    /// <summary>Arrival time of train <paramref name="n"/> (0-based), seconds from the run start.</summary>
    public double ArrivalS(int n) => OffsetS + (n * HeadwayS);

    /// <summary>"every 6:00 from 0:30, doors 45 s, 150 off, 300 places".</summary>
    public string Describe()
    {
        CultureInfo c = CultureInfo.InvariantCulture;
        static string Clock(double s)
        {
            TimeSpan t = TimeSpan.FromSeconds(Math.Max(0, Math.Round(s)));
            return string.Create(CultureInfo.InvariantCulture, $"{(int)t.TotalMinutes}:{t.Seconds:00}");
        }

        return string.Create(c, $"every {Clock(HeadwayS)} from {Clock(OffsetS)}, doors {DwellS:0} s, {AlightPerTrain} off, {Capacity} places");
    }
}
