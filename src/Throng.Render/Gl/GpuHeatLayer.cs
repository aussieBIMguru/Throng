using OpenTK.Graphics.OpenGL4;
using Throng.Render.Scene;

namespace Throng.Render.Gl;

/// <summary>
/// A live heatmap layer on the GPU (D-202): a texture refreshed whenever the map changes and a quad lifted a few
/// centimetres off the surface so it reads over the floor fill.
/// </summary>
internal sealed class GpuHeatLayer : IDisposable
{
    private const int FloatsPerVertex = 5;
    private const float LiftM = 0.03f;
    private int _texture;
    private int _vao;
    private int _vbo;

    private GpuHeatLayer(int width, int height)
    {
        Width = width;
        Height = height;
        _texture = GL.GenTexture();
        GL.BindTexture(TextureTarget.Texture2D, _texture);
        GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba8, width, height, 0, PixelFormat.Bgra, PixelType.UnsignedByte, IntPtr.Zero);

        // Linear filtering blends neighbouring cells, which reads as a smooth map rather than a chessboard.
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
        GL.BindTexture(TextureTarget.Texture2D, 0);

        _vao = GL.GenVertexArray();
        _vbo = GL.GenBuffer();
        GL.BindVertexArray(_vao);
        GL.BindBuffer(BufferTarget.ArrayBuffer, _vbo);
        GL.BufferData(BufferTarget.ArrayBuffer, 6 * FloatsPerVertex * sizeof(float), IntPtr.Zero, BufferUsageHint.DynamicDraw);
        int stride = FloatsPerVertex * sizeof(float);
        GL.EnableVertexAttribArray(0);
        GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, stride, 0);
        GL.EnableVertexAttribArray(1);
        GL.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, stride, 3 * sizeof(float));
        GL.BindVertexArray(0);
        GL.BindBuffer(BufferTarget.ArrayBuffer, 0);
    }

    /// <summary>Texture width.</summary>
    public int Width { get; }

    /// <summary>Texture height.</summary>
    public int Height { get; }

    /// <summary>Creates the texture and quad for a layer size.</summary>
    public static GpuHeatLayer Create(int width, int height) => new(width, height);

    /// <summary>Uploads the layer's pixels and corners.</summary>
    public void Update(HeatLayerDraw draw)
    {
        GL.BindTexture(TextureTarget.Texture2D, _texture);
        GL.PixelStore(PixelStoreParameter.UnpackAlignment, 4);
        GL.TexSubImage2D(TextureTarget.Texture2D, 0, 0, 0, Width, Height, PixelFormat.Bgra, PixelType.UnsignedByte, draw.Bgra);
        GL.BindTexture(TextureTarget.Texture2D, 0);

        IReadOnlyList<Throng.Core.Geometry.Point3> c = draw.Corners;
        float[] data =
        [
            (float)c[0].X, (float)c[0].Y, (float)c[0].Z + LiftM, 0f, 0f,
            (float)c[1].X, (float)c[1].Y, (float)c[1].Z + LiftM, 1f, 0f,
            (float)c[2].X, (float)c[2].Y, (float)c[2].Z + LiftM, 1f, 1f,
            (float)c[0].X, (float)c[0].Y, (float)c[0].Z + LiftM, 0f, 0f,
            (float)c[2].X, (float)c[2].Y, (float)c[2].Z + LiftM, 1f, 1f,
            (float)c[3].X, (float)c[3].Y, (float)c[3].Z + LiftM, 0f, 1f,
        ];
        GL.BindBuffer(BufferTarget.ArrayBuffer, _vbo);
        GL.BufferSubData(BufferTarget.ArrayBuffer, IntPtr.Zero, data.Length * sizeof(float), data);
        GL.BindBuffer(BufferTarget.ArrayBuffer, 0);
    }

    /// <summary>Draws the quad with whatever textured program is bound (texture unit 0).</summary>
    public void Draw()
    {
        GL.ActiveTexture(TextureUnit.Texture0);
        GL.BindTexture(TextureTarget.Texture2D, _texture);
        GL.BindVertexArray(_vao);
        GL.DrawArrays(PrimitiveType.Triangles, 0, 6);
        GL.BindTexture(TextureTarget.Texture2D, 0);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_texture != 0)
        {
            GL.DeleteTexture(_texture);
            GL.DeleteBuffer(_vbo);
            GL.DeleteVertexArray(_vao);
            _texture = 0;
            _vbo = 0;
            _vao = 0;
        }
    }
}
