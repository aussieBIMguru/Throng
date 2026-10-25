namespace Throng.Core.Geometry;

/// <summary>
/// The queue slots in front of a device (D-153, D-163): one line per lane (server) and side, and, when the device has
/// a drawn <see cref="Device.QueuePath"/>, one shared snake on the service side feeding every lane.
/// </summary>
/// <param name="Lines">
/// Slots per line (line = side × lanes + lane; side 0 is the service side, side 1 the far side of pass-through
/// devices). Slot 0 is the lane's service point. With a snake, each service-side line holds only its service point.
/// </param>
/// <param name="Snake">Slots of the shared snake along the drawn path (slot 0 first in line), or null when there is none.</param>
public sealed record QueueLineSet(Point2[][] Lines, Point2[]? Snake);
