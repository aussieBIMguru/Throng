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
    private int _timeLocation;
    private int _colorModeLocation;
    private int _dimLocation = -1;
    private int _warpLocation = -1;
    private int _shiftLocation = -1;
    private int _gapLocation = -1;
    private int _levelCountLocation = -1;
    private int _levelZLocation = -1;
    private int _clipLocation = -1;
    private int _boxMinLocation = -1;
    private int _boxMaxLocation = -1;

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
        _timeLocation = GL.GetUniformLocation(_handle, "uTime");
        _colorModeLocation = GL.GetUniformLocation(_handle, "uColorMode");
        _dimLocation = GL.GetUniformLocation(_handle, "uDim");
        _warpLocation = GL.GetUniformLocation(_handle, "uWarp");
        _shiftLocation = GL.GetUniformLocation(_handle, "uShift");
        _gapLocation = GL.GetUniformLocation(_handle, "uGap");
        _levelCountLocation = GL.GetUniformLocation(_handle, "uLevelCount");
        _levelZLocation = GL.GetUniformLocation(_handle, "uLevelZ");
        _clipLocation = GL.GetUniformLocation(_handle, "uClip");
        _boxMinLocation = GL.GetUniformLocation(_handle, "uBoxMin");
        _boxMaxLocation = GL.GetUniformLocation(_handle, "uBoxMax");
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

    /// <summary>Seconds for animated effects (the agent program's stuck ring).</summary>
    public void SetTime(float seconds) => GL.Uniform1(_timeLocation, seconds);

    /// <summary>Sets <c>uColorMode</c> (agents: 0 profile, 1 stress; D-146).</summary>
    public void SetColorMode(int mode) => GL.Uniform1(_colorModeLocation, mode);

    /// <summary>Sets <c>uDim</c> (agents drawn smaller and greyer over a heatmap, D-207; 0 = normal).</summary>
    public void SetDim(float dim) => GL.Uniform1(_dimLocation, dim);

    /// <summary>
    /// Exploded levels (D-264): the distinct level elevations (lowest first, at most <see cref="Shaders.MaxLevels"/>)
    /// and the extra gap between consecutive levels. Program must be in use.
    /// </summary>
    public void SetExplode(float[] levelZ, int count, float gap)
    {
        count = Math.Clamp(count, 0, Math.Min(levelZ.Length, Shaders.MaxLevels));
        GL.Uniform1(_levelCountLocation, count);
        GL.Uniform1(_gapLocation, gap);
        if (count > 0 && _levelZLocation >= 0)
        {
            GL.Uniform1(_levelZLocation, count, levelZ);
        }
    }

    /// <summary>How heights are remapped (D-264): 0 none, 1 stretch across storeys, 2 a rigid shift of <paramref name="shift"/> m.</summary>
    public void SetWarp(int mode, float shift = 0f)
    {
        GL.Uniform1(_warpLocation, mode);
        GL.Uniform1(_shiftLocation, shift);
    }

    /// <summary>Cutaway (D-266): discard fragments outside the box while <paramref name="on"/>. Program must be in use.</summary>
    public void SetClip(bool on, Vector3 min, Vector3 max)
    {
        GL.Uniform1(_clipLocation, on ? 1 : 0);
        GL.Uniform3(_boxMinLocation, min.X, min.Y, min.Z);
        GL.Uniform3(_boxMaxLocation, max.X, max.Y, max.Z);
    }

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
