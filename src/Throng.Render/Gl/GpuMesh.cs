using OpenTK.Graphics.OpenGL4;
using Throng.Render.Scene;

namespace Throng.Render.Gl;

/// <summary>A <see cref="MeshData"/> uploaded to a vertex array + buffer.</summary>
internal sealed class GpuMesh : IDisposable
{
    private readonly int _vao;
    private readonly int _vbo;
    private readonly int _vertexCount;
    private readonly PrimitiveType _primitive;

    private GpuMesh(int vao, int vbo, int vertexCount, PrimitiveType primitive)
    {
        _vao = vao;
        _vbo = vbo;
        _vertexCount = vertexCount;
        _primitive = primitive;
    }

    /// <summary>Uploads mesh data; returns null for an empty mesh (nothing to draw).</summary>
    public static GpuMesh? Create(MeshData data)
    {
        if (data.VertexCount == 0)
        {
            return null;
        }

        int vao = GL.GenVertexArray();
        int vbo = GL.GenBuffer();
        GL.BindVertexArray(vao);
        GL.BindBuffer(BufferTarget.ArrayBuffer, vbo);
        GL.BufferData(BufferTarget.ArrayBuffer, data.Vertices.Length * sizeof(float), data.Vertices, BufferUsageHint.StaticDraw);

        int stride = MeshData.FloatsPerVertex * sizeof(float);
        GL.EnableVertexAttribArray(0);
        GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, stride, 0);
        GL.EnableVertexAttribArray(1);
        GL.VertexAttribPointer(1, 4, VertexAttribPointerType.Float, false, stride, 3 * sizeof(float));

        GL.BindVertexArray(0);
        GL.BindBuffer(BufferTarget.ArrayBuffer, 0);

        PrimitiveType primitive = data.Primitive == MeshPrimitive.Lines ? PrimitiveType.Lines : PrimitiveType.Triangles;
        return new GpuMesh(vao, vbo, data.VertexCount, primitive);
    }

    /// <summary>Draws the mesh with whatever program is bound.</summary>
    public void Draw()
    {
        GL.BindVertexArray(_vao);
        GL.DrawArrays(_primitive, 0, _vertexCount);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        GL.DeleteBuffer(_vbo);
        GL.DeleteVertexArray(_vao);
    }
}
