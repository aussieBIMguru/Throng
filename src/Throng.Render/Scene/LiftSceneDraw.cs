namespace Throng.Render.Scene;

/// <summary>
/// Everything lift-related to draw in 3D while running (round 014): the cars and the sliding doors (car and landing).
/// The renderer rebuilds its meshes only when a different instance is passed.
/// </summary>
/// <param name="Cars">The cars on shown levels.</param>
/// <param name="Doors">Car doors and landing doors on shown levels.</param>
public sealed record LiftSceneDraw(IReadOnlyList<LiftCarDraw> Cars, IReadOnlyList<LiftDoorDraw> Doors);
