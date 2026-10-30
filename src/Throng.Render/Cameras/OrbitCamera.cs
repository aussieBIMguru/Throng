using System.Numerics;
using Throng.Core.Geometry;

namespace Throng.Render.Cameras;

/// <summary>
/// Orbit camera around a target point, Z up (§12.1): perspective for 3D, or orthographic at a fixed 2:1 game iso
/// angle for ISO (round 015, D-263), where <see cref="Distance"/> sets the zoom.
/// </summary>
public sealed class OrbitCamera
{
    private const float MinPitch = 0.05f;
    private const float MaxPitch = 1.53f;
    private const float FieldOfView = MathF.PI / 4f;

    /// <summary>2:1 game iso: the view looks down atan(½) ≈ 26.57°, so a ground square's diagonals read 2 : 1.</summary>
    public static readonly float IsoPitch = MathF.Atan(0.5f);

    /// <summary>Iso preset names, by where the eye sits (index 0–3; ] steps forward).</summary>
    public static readonly string[] IsoPresetNames = ["SW", "SE", "NE", "NW"];

    /// <summary>Where an orthographic eye sits back from the target, metres (so nothing in front is cut off).</summary>
    private const float OrthoEyeDistance = 4000f;

    /// <summary>True for an orthographic (ISO) camera.</summary>
    public bool Orthographic { get; init; }

    /// <summary>World point the camera orbits.</summary>
    public Vector3 Target { get; set; } = Vector3.Zero;

    /// <summary>Heading around Z in radians (0 = looking from +X).</summary>
    public float Yaw { get; set; } = -2.2f;

    /// <summary>Elevation angle above the horizon in radians.</summary>
    public float Pitch { get; set; } = 0.62f;

    /// <summary>Distance from target to eye in metres.</summary>
    public float Distance { get; set; } = 90f;

    /// <summary>Eye position in world space.</summary>
    public Vector3 Eye =>
        Target + (Distance * new Vector3(MathF.Cos(Pitch) * MathF.Cos(Yaw), MathF.Cos(Pitch) * MathF.Sin(Yaw), MathF.Sin(Pitch)));

    /// <summary>Combined view-projection matrix (System.Numerics row-vector convention).</summary>
    public Matrix4x4 ViewProjection(double widthDip, double heightDip)
    {
        float aspect = (float)(Math.Max(1, widthDip) / Math.Max(1, heightDip));
        if (Orthographic)
        {
            Vector3 back = Vector3.Normalize(Eye - Target);
            Matrix4x4 isoView = Matrix4x4.CreateLookAt(Target + (back * OrthoEyeDistance), Target, Vector3.UnitZ);
            float height = ViewHeightM;
            Matrix4x4 ortho = Matrix4x4.CreateOrthographic(height * aspect, height, 1f, OrthoEyeDistance * 2f);
            return isoView * ortho;
        }

        Matrix4x4 view = Matrix4x4.CreateLookAt(Eye, Target, Vector3.UnitZ);
        float near = MathF.Max(0.05f, Distance * 0.01f);
        float far = (Distance * 10f) + 1000f;
        Matrix4x4 projection = Matrix4x4.CreatePerspectiveFieldOfView(FieldOfView, aspect, near, far);
        return view * projection;
    }

    /// <summary>
    /// Height of the view at the target, metres: what the perspective view spans there, and the orthographic view's
    /// height, so zoom feels the same in 3D and ISO.
    /// </summary>
    public float ViewHeightM => 2f * Distance * MathF.Tan(FieldOfView / 2f);

    /// <summary>Points the camera from one of the four iso presets (0 SW, 1 SE, 2 NE, 3 NW; wraps) at the 2:1 angle.</summary>
    public void SetIsoPreset(int preset)
    {
        int k = ((preset % 4) + 4) % 4;
        Yaw = (-3f * MathF.PI / 4f) + (k * MathF.PI / 2f);
        Pitch = IsoPitch;
    }

    /// <summary>Rotates by a screen drag in DIPs (ignored by the fixed-angle ISO camera).</summary>
    public void Orbit(double dxDip, double dyDip)
    {
        if (Orthographic)
        {
            return;
        }

        Yaw -= (float)dxDip * 0.008f;
        Pitch = Math.Clamp(Pitch + ((float)dyDip * 0.008f), MinPitch, MaxPitch);
    }

    /// <summary>Slides the target by a screen drag in DIPs (content follows the cursor).</summary>
    public void Pan(double dxDip, double dyDip)
    {
        Vector3 forward = Vector3.Normalize(Target - Eye);
        Vector3 right = Vector3.Normalize(Vector3.Cross(forward, Vector3.UnitZ));
        Vector3 up = Vector3.Cross(right, forward);
        float scale = Distance * 0.0018f;
        Target += ((-right * (float)dxDip) + (up * (float)dyDip)) * scale;
    }

    /// <summary>Moves the eye towards (factor &lt; 1) or away from the target.</summary>
    public void Zoom(double factor) => Distance = Math.Clamp(Distance * (float)factor, 2f, 5000f);

    /// <summary>Frames plan bounds and an elevation range.</summary>
    public void Fit(Bounds2 bounds, double minZ, double maxZ)
    {
        if (bounds.IsEmpty)
        {
            Target = Vector3.Zero;
            Distance = 90f;
            return;
        }

        Point2 c = bounds.Center;
        Target = new Vector3((float)c.X, (float)c.Y, (float)((minZ + maxZ) * 0.5));
        double extent = Math.Max(Math.Max(bounds.Width, bounds.Height), maxZ - minZ);
        Distance = (float)((extent * 1.35) + 10);
    }
}
