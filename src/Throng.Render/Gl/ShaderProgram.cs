using System.Numerics;
using OpenTK.Graphics.OpenGL4;
using Throng.Core.Common;

namespace Throng.Render.Gl;

/// <summary>
/// A linked GLSL program built from the sources in <see cref="Shaders"/>: the flat-colour program for triangles,
/// and the thick-line program (flat vertex shader + a geometry shader that expands each line into a screen-space
/// quad, D-049). Uniforms a program doesn't declare are ignored by GL, so every setter is safe on either program.
/// </summary>
internal sealed class ShaderProgram : IDisposable
{
    private readonly string _vertexSource;
    private readonly string? _geometrySource;
    private readonly string _fragmentSource;
    private readonly float[] _matrixBuffer = new float[16];
    private int _handle;
    private int _viewProjectionLocation;
    private int _alphaLocation;
    private int _viewportLocation;
    private int _lineWidthLocation;

    /// <summary>Creates a program from vertex, optional geometry, and fragment sources (compiled in <see cref="Initialise"/>).</summary>
    public ShaderProgram(string vertexSource, string? geometrySource, string fragmentSource)
    {
        _vertexSource = vertexSource;
        _geometrySource = geometrySource;
        _fragmentSource = fragmentSource;
    }

    /// <summary>Compiles and links. Must be called with a current GL context.</summary>
    public Result Initialise()
    {
        int vertex = Compile(ShaderType.VertexShader, _vertexSource, out string? vertexError);
        if (vertexError is not null)
        {
            return Result.Fail("Vertex shader: " + vertexError);
        }

        int geometry = 0;
        if (_geometrySource is not null)
        {
            geometry = Compile(ShaderType.GeometryShader, _geometrySource, out string? geometryError);
            if (geometryError is not null)
            {
                GL.DeleteShader(vertex);
                return Result.Fail("Geometry shader: " + geometryError);
            }
        }

        int fragment = Compile(ShaderType.FragmentShader, _fragmentSource, out string? fragmentError);
        if (fragmentError is not null)
        {
            GL.DeleteShader(vertex);
            if (geometry != 0)
            {
                GL.DeleteShader(geometry);
            }

            return Result.Fail("Fragment shader: " + fragmentError);
        }

        _handle = GL.CreateProgram();
        GL.AttachShader(_handle, vertex);
        if (geometry != 0)
        {
            GL.AttachShader(_handle, geometry);
        }

        GL.AttachShader(_handle, fragment);
        GL.LinkProgram(_handle);
        GL.GetProgram(_handle, GetProgramParameterName.LinkStatus, out int linked);
        GL.DetachShader(_handle, vertex);
        GL.DetachShader(_handle, fragment);
        GL.DeleteShader(vertex);
        GL.DeleteShader(fragment);
        if (geometry != 0)
        {
            GL.DetachShader(_handle, geometry);
            GL.DeleteShader(geometry);
        }

        if (linked == 0)
        {
            string log = GL.GetProgramInfoLog(_handle);
            GL.DeleteProgram(_handle);
            _handle = 0;
            return Result.Fail("Shader link: " + log);
        }

        _viewProjectionLocation = GL.GetUniformLocation(_handle, "uViewProjection");
        _alphaLocation = GL.GetUniformLocation(_handle, "uAlpha");
        _viewportLocation = GL.GetUniformLocation(_handle, "uViewport");
        _lineWidthLocation = GL.GetUniformLocation(_handle, "uLineWidth");
        return Result.Ok();
    }

    /// <summary>Binds the program.</summary>
    public void Use() => GL.UseProgram(_handle);

    /// <summary>
    /// Uploads the view-projection matrix. System.Numerics matrices are row-major with row vectors; passed
    /// untransposed, GL reads them as the column-major equivalent for <c>M * v</c> in the shader.
    /// </summary>
    public void SetViewProjection(Matrix4x4 m)
    {
        _matrixBuffer[0] = m.M11; _matrixBuffer[1] = m.M12; _matrixBuffer[2] = m.M13; _matrixBuffer[3] = m.M14;
        _matrixBuffer[4] = m.M21; _matrixBuffer[5] = m.M22; _matrixBuffer[6] = m.M23; _matrixBuffer[7] = m.M24;
        _matrixBuffer[8] = m.M31; _matrixBuffer[9] = m.M32; _matrixBuffer[10] = m.M33; _matrixBuffer[11] = m.M34;
        _matrixBuffer[12] = m.M41; _matrixBuffer[13] = m.M42; _matrixBuffer[14] = m.M43; _matrixBuffer[15] = m.M44;
        GL.UniformMatrix4(_viewProjectionLocation, 1, false, _matrixBuffer);
    }

    /// <summary>Sets the global alpha multiplier.</summary>
    public void SetAlpha(float alpha) => GL.Uniform1(_alphaLocation, alpha);

    /// <summary>Framebuffer size in pixels (thick-line program).</summary>
    public void SetViewport(int width, int height) => GL.Uniform2(_viewportLocation, (float)Math.Max(1, width), (float)Math.Max(1, height));

    /// <summary>Line width in framebuffer pixels (thick-line program).</summary>
    public void SetLineWidth(float pixels) => GL.Uniform1(_lineWidthLocation, pixels);

    /// <inheritdoc />
    public void Dispose()
    {
        if (_handle != 0)
        {
            GL.DeleteProgram(_handle);
            _handle = 0;
        }
    }

    private static int Compile(ShaderType type, string source, out string? error)
    {
        int shader = GL.CreateShader(type);
        GL.ShaderSource(shader, source);
        GL.CompileShader(shader);
        GL.GetShader(shader, ShaderParameter.CompileStatus, out int status);
        if (status == 0)
        {
            error = GL.GetShaderInfoLog(shader);
            GL.DeleteShader(shader);
            return 0;
        }

        error = null;
        return shader;
    }
}
