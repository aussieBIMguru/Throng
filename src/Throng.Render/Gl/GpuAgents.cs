using OpenTK.Graphics.OpenGL4;

namespace Throng.Render.Gl;

/// <summary>
/// Instanced agent drawing (D-102): one unit capsule mesh and a per-frame instance buffer, drawn in a single
/// instanced call. Instances use the simulation's snapshot layout directly (10 floats each: x, y, z, heading, r, g,
/// b, radius, height, stuck), so the app can upload what it copied from the runner without repacking.
/// </summary>
internal sealed class GpuAgents : IDisposable
{
    /// <summary>Floats per instance (matches <c>SimEngine.FloatsPerAgent</c>).</summary>
    public const int FloatsPerInstance = 10;

    private const int Sides = 12;
    private const int FloatsPerVertex = 5;

    private readonly int _vao;
    private readonly int _meshVbo;
    private readonly int _instanceVbo;
    private readonly int _vertexCount;
    private int _instanceCapacity;
    private int _instanceCount;

    private GpuAgents(int vao, int meshVbo, int instanceVbo, int vertexCount)
    {
        _vao = vao;
        _meshVbo = meshVbo;
        _instanceVbo = instanceVbo;
        _vertexCount = vertexCount;
    }

    /// <summary>Creates the mesh and buffers (GL context current).</summary>
    public static GpuAgents Create()
    {
        float[] mesh = BuildMesh();
        int vao = GL.GenVertexArray();
        int meshVbo = GL.GenBuffer();
        int instanceVbo = GL.GenBuffer();
        GL.BindVertexArray(vao);

        GL.BindBuffer(BufferTarget.ArrayBuffer, meshVbo);
        GL.BufferData(BufferTarget.ArrayBuffer, mesh.Length * sizeof(float), mesh, BufferUsageHint.StaticDraw);
        int stride = FloatsPerVertex * sizeof(float);
        GL.EnableVertexAttribArray(0);
        GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, stride, 0);
        GL.EnableVertexAttribArray(1);
        GL.VertexAttribPointer(1, 1, VertexAttribPointerType.Float, false, stride, 3 * sizeof(float));
        GL.EnableVertexAttribArray(2);
        GL.VertexAttribPointer(2, 1, VertexAttribPointerType.Float, false, stride, 4 * sizeof(float));

        GL.BindBuffer(BufferTarget.ArrayBuffer, instanceVbo);
        GL.BufferData(BufferTarget.ArrayBuffer, 64 * FloatsPerInstance * sizeof(float), IntPtr.Zero, BufferUsageHint.StreamDraw);
        int istride = FloatsPerInstance * sizeof(float);
        GL.EnableVertexAttribArray(3);
        GL.VertexAttribPointer(3, 4, VertexAttribPointerType.Float, false, istride, 0);
        GL.VertexAttribDivisor(3, 1);
        GL.EnableVertexAttribArray(4);
        GL.VertexAttribPointer(4, 3, VertexAttribPointerType.Float, false, istride, 4 * sizeof(float));
        GL.VertexAttribDivisor(4, 1);
        GL.EnableVertexAttribArray(5);
        GL.VertexAttribPointer(5, 3, VertexAttribPointerType.Float, false, istride, 7 * sizeof(float));
        GL.VertexAttribDivisor(5, 1);

        GL.BindVertexArray(0);
        GL.BindBuffer(BufferTarget.ArrayBuffer, 0);
        return new GpuAgents(vao, meshVbo, instanceVbo, mesh.Length / FloatsPerVertex) { _instanceCapacity = 64 };
    }

    /// <summary>Replaces the instances with the first <paramref name="count"/> of <paramref name="data"/>.</summary>
    public void Update(float[] data, int count)
    {
        count = Math.Clamp(count, 0, data.Length / FloatsPerInstance);
        _instanceCount = count;
        if (count == 0)
        {
            return;
        }

        GL.BindBuffer(BufferTarget.ArrayBuffer, _instanceVbo);
        if (count > _instanceCapacity)
        {
            _instanceCapacity = Math.Max(count, _instanceCapacity * 2);
            GL.BufferData(BufferTarget.ArrayBuffer, _instanceCapacity * FloatsPerInstance * sizeof(float), IntPtr.Zero, BufferUsageHint.StreamDraw);
        }

        GL.BufferSubData(BufferTarget.ArrayBuffer, IntPtr.Zero, count * FloatsPerInstance * sizeof(float), data);
        GL.BindBuffer(BufferTarget.ArrayBuffer, 0);
    }

    /// <summary>Draws every instance with whatever program is bound.</summary>
    public void Draw()
    {
        if (_instanceCount == 0)
        {
            return;
        }

        GL.BindVertexArray(_vao);
        GL.DrawArraysInstanced(PrimitiveType.Triangles, 0, _vertexCount, _instanceCount);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        GL.DeleteBuffer(_meshVbo);
        GL.DeleteBuffer(_instanceVbo);
        GL.DeleteVertexArray(_vao);
    }

    /// <summary>
    /// Unit agent facing +X: a 12-sided body (radius 1, to 0.78 of the height), a narrower head with a domed top,
    /// a front nub, and a flat ring at floor level (part 3, shown when stuck). Parts: 0 body, 1 head, 2 nub, 3 ring.
    /// </summary>
    private static float[] BuildMesh()
    {
        var v = new List<float>(2048);
        void Vertex(double x, double y, double z, double shade, int part)
        {
            v.Add((float)x);
            v.Add((float)y);
            v.Add((float)z);
            v.Add((float)shade);
            v.Add(part);
        }

        // Fixed light from the south-west, above: shade = ambient + diffuse.
        double Shade(double nx, double ny, double nz)
        {
            const double lx = -0.45, ly = -0.55, lz = 0.70;
            return 0.45 + (0.55 * Math.Max(0, (nx * lx) + (ny * ly) + (nz * lz)));
        }

        void Prism(double r, double z0, double z1, int part, bool cap)
        {
            for (int k = 0; k < Sides; k++)
            {
                double a0 = 2 * Math.PI * k / Sides, a1 = 2 * Math.PI * (k + 1) / Sides;
                double am = (a0 + a1) * 0.5;
                double sh = Shade(Math.Cos(am), Math.Sin(am), 0);
                double x0 = Math.Cos(a0) * r, y0 = Math.Sin(a0) * r, x1 = Math.Cos(a1) * r, y1 = Math.Sin(a1) * r;
                Vertex(x0, y0, z0, sh, part);
                Vertex(x1, y1, z0, sh, part);
                Vertex(x1, y1, z1, sh, part);
                Vertex(x0, y0, z0, sh, part);
                Vertex(x1, y1, z1, sh, part);
                Vertex(x0, y0, z1, sh, part);
                if (cap)
                {
                    double top = Shade(0, 0, 1);
                    Vertex(0, 0, z1, top, part);
                    Vertex(x0, y0, z1, top, part);
                    Vertex(x1, y1, z1, top, part);
                }
            }
        }

        // Body and shoulders.
        Prism(1.0, 0.0, 0.78, part: 0, cap: true);

        // Head: a short prism with a domed (conical) top.
        const double headR = 0.55, headZ0 = 0.80, headZ1 = 0.93, crown = 1.0;
        Prism(headR, headZ0, headZ1, part: 1, cap: false);
        for (int k = 0; k < Sides; k++)
        {
            double a0 = 2 * Math.PI * k / Sides, a1 = 2 * Math.PI * (k + 1) / Sides;
            double am = (a0 + a1) * 0.5;
            double sh = Shade(Math.Cos(am) * 0.6, Math.Sin(am) * 0.6, 0.8);
            Vertex(Math.Cos(a0) * headR, Math.Sin(a0) * headR, headZ1, sh, 1);
            Vertex(Math.Cos(a1) * headR, Math.Sin(a1) * headR, headZ1, sh, 1);
            Vertex(0, 0, crown, sh, 1);
        }

        // Front nub: a small wedge on the +X side at chest height, so facing reads in plan and 3D.
        double nubShade = Shade(1, 0, 0.4);
        Vertex(0.85, -0.35, 0.60, nubShade, 2);
        Vertex(1.35, 0.0, 0.70, nubShade, 2);
        Vertex(0.85, 0.35, 0.60, nubShade, 2);
        Vertex(0.85, -0.35, 0.78, nubShade, 2);
        Vertex(1.35, 0.0, 0.79, nubShade, 2);
        Vertex(0.85, 0.35, 0.78, nubShade, 2);
        Vertex(0.85, -0.35, 0.60, nubShade, 2);
        Vertex(1.35, 0.0, 0.70, nubShade, 2);
        Vertex(1.35, 0.0, 0.79, nubShade, 2);
        Vertex(0.85, -0.35, 0.60, nubShade, 2);
        Vertex(1.35, 0.0, 0.79, nubShade, 2);
        Vertex(0.85, -0.35, 0.78, nubShade, 2);
        Vertex(0.85, 0.35, 0.60, nubShade, 2);
        Vertex(1.35, 0.0, 0.79, nubShade, 2);
        Vertex(1.35, 0.0, 0.70, nubShade, 2);
        Vertex(0.85, 0.35, 0.60, nubShade, 2);
        Vertex(0.85, 0.35, 0.78, nubShade, 2);
        Vertex(1.35, 0.0, 0.79, nubShade, 2);

        // Stuck ring: annulus at a fixed 0.02 m above the floor (z isn't scaled by height for part 3).
        const double inner = 1.2, outer = 1.5, ringZ = 0.02;
        for (int k = 0; k < Sides * 2; k++)
        {
            double a0 = Math.PI * k / Sides, a1 = Math.PI * (k + 1) / Sides;
            double ix0 = Math.Cos(a0) * inner, iy0 = Math.Sin(a0) * inner, ix1 = Math.Cos(a1) * inner, iy1 = Math.Sin(a1) * inner;
            double ox0 = Math.Cos(a0) * outer, oy0 = Math.Sin(a0) * outer, ox1 = Math.Cos(a1) * outer, oy1 = Math.Sin(a1) * outer;
            Vertex(ix0, iy0, ringZ, 1, 3);
            Vertex(ox0, oy0, ringZ, 1, 3);
            Vertex(ox1, oy1, ringZ, 1, 3);
            Vertex(ix0, iy0, ringZ, 1, 3);
            Vertex(ox1, oy1, ringZ, 1, 3);
            Vertex(ix1, iy1, ringZ, 1, 3);
        }

        return [.. v];
    }
}
