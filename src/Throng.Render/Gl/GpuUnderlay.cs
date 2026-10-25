using OpenTK.Graphics.OpenGL4;
using Throng.Render.Scene;

namespace Throng.Render.Gl;

/// <summary>
/// A plan underlay on the GPU (D-085): an RGBA texture plus a textured quad. The texture is kept across scene
/// rebuilds while the same pixel buffer is supplied; only the quad is rebuilt when the underlay moves.
/// </summary>
internal sealed class GpuUnderlay : IDisposable
{
    private const int FloatsPerVertex = 5;
    private int _texture;
    private int _vao;
    private int _vbo;

    private GpuUnderlay(UnderlayPixels pixels)
    {
        Pixels = pixels;
        _texture = GL.GenTexture();
        GL.BindTexture(TextureTarget.Texture2D, _texture);
        GL.PixelStore(PixelStoreParameter.UnpackAlignment, 4);
        GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba8, pixels.Width, pixels.Height, 0,
            PixelFormat.Bgra, PixelType.UnsignedByte, pixels.Bgra);
        GL.GenerateMipmap(GenerateMipmapTarget.Texture2D);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.LinearMipmapLinear);
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

    /// <summary>The pixel buffer the texture was made from (identity decides reuse).</summary>
    public UnderlayPixels Pixels { get; }

    /// <summary>Opacity for the next draw.</summary>
    public float Opacity { get; private set; } = 1f;

    /// <summary>Creates the texture and quad buffers.</summary>
    public static GpuUnderlay Create(UnderlayPixels pixels) => new(pixels);

    /// <summary>Uploads the quad corners (bottom-left, bottom-right, top-right, top-left) and opacity.</summary>
    public void Update(UnderlayDraw draw)
    {
        Opacity = draw.Opacity;
        IReadOnlyList<Throng.Core.Geometry.Point3> c = draw.Corners;
        float[] data =
        [
            (float)c[0].X, (float)c[0].Y, (float)c[0].Z, 0f, 1f,
            (float)c[1].X, (float)c[1].Y, (float)c[1].Z, 1f, 1f,
            (float)c[2].X, (float)c[2].Y, (float)c[2].Z, 1f, 0f,
            (float)c[0].X, (float)c[0].Y, (float)c[0].Z, 0f, 1f,
            (float)c[2].X, (float)c[2].Y, (float)c[2].Z, 1f, 0f,
            (float)c[3].X, (float)c[3].Y, (float)c[3].Z, 0f, 0f,
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
