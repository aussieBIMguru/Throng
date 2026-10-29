using OpenTK.Graphics.OpenGL4;
using Throng.Core.Common;

namespace Throng.Render.Gl;

/// <summary>
/// Draws one frame into an offscreen framebuffer and reads it back (round 012 PNG export, D-234). Call on the GL
/// thread with the context current (inside the viewport's render callback); the caller's framebuffer and viewport are
/// restored afterwards.
/// </summary>
public static class OffscreenCapture
{
    /// <summary>Largest width or height the driver can render to, pixels (0 if unknown). Call with a context current.</summary>
    public static int MaxSize() => GL.GetInteger(GetPName.MaxRenderbufferSize);

    /// <summary>
    /// Renders <paramref name="draw"/> at <paramref name="width"/> × <paramref name="height"/> with
    /// <paramref name="samples"/>× MSAA and returns the pixels as BGRA, top row first, fully opaque.
    /// </summary>
    /// <param name="width">Pixel width.</param>
    /// <param name="height">Pixel height.</param>
    /// <param name="samples">MSAA samples (clamped to what the driver supports; 0 or 1 for none).</param>
    /// <param name="draw">Draws the frame; the framebuffer is bound and the viewport set when it's called.</param>
    public static Result<byte[]> Capture(int width, int height, int samples, Action draw)
    {
        if (width < 1 || height < 1)
        {
            return Result<byte[]>.Fail("Nothing to capture: the view has no size.");
        }

        int prevDraw = GL.GetInteger(GetPName.DrawFramebufferBinding);
        int prevRead = GL.GetInteger(GetPName.ReadFramebufferBinding);
        int[] prevViewport = new int[4];
        GL.GetInteger(GetPName.Viewport, prevViewport);

        int maxSamples = GL.GetInteger(GetPName.MaxSamples);
        int msaa = Math.Clamp(samples, 0, Math.Max(0, maxSamples));
        if (msaa < 2)
        {
            msaa = 0;
        }

        int msFbo = 0, msColour = 0, msDepth = 0, outFbo = 0, outColour = 0;
        try
        {
            // The frame is drawn into a multisampled target (when available), then resolved into a plain one to read.
            msFbo = GL.GenFramebuffer();
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, msFbo);
            msColour = GL.GenRenderbuffer();
            GL.BindRenderbuffer(RenderbufferTarget.Renderbuffer, msColour);
            GL.RenderbufferStorageMultisample(RenderbufferTarget.Renderbuffer, msaa, RenderbufferStorage.Rgba8, width, height);
            GL.FramebufferRenderbuffer(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, RenderbufferTarget.Renderbuffer, msColour);
            msDepth = GL.GenRenderbuffer();
            GL.BindRenderbuffer(RenderbufferTarget.Renderbuffer, msDepth);
            GL.RenderbufferStorageMultisample(RenderbufferTarget.Renderbuffer, msaa, RenderbufferStorage.Depth24Stencil8, width, height);
            GL.FramebufferRenderbuffer(FramebufferTarget.Framebuffer, FramebufferAttachment.DepthStencilAttachment, RenderbufferTarget.Renderbuffer, msDepth);
            if (GL.CheckFramebufferStatus(FramebufferTarget.Framebuffer) != FramebufferErrorCode.FramebufferComplete)
            {
                return Result<byte[]>.Fail($"The graphics driver couldn't make a {width} × {height} image.");
            }

            GL.Viewport(0, 0, width, height);
            draw();

            outFbo = GL.GenFramebuffer();
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, outFbo);
            outColour = GL.GenRenderbuffer();
            GL.BindRenderbuffer(RenderbufferTarget.Renderbuffer, outColour);
            GL.RenderbufferStorage(RenderbufferTarget.Renderbuffer, RenderbufferStorage.Rgba8, width, height);
            GL.FramebufferRenderbuffer(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, RenderbufferTarget.Renderbuffer, outColour);
            if (GL.CheckFramebufferStatus(FramebufferTarget.Framebuffer) != FramebufferErrorCode.FramebufferComplete)
            {
                return Result<byte[]>.Fail($"The graphics driver couldn't make a {width} × {height} image.");
            }

            GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer, msFbo);
            GL.BindFramebuffer(FramebufferTarget.DrawFramebuffer, outFbo);
            GL.BlitFramebuffer(0, 0, width, height, 0, 0, width, height, ClearBufferMask.ColorBufferBit, BlitFramebufferFilter.Nearest);

            GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer, outFbo);
            GL.PixelStore(PixelStoreParameter.PackAlignment, 4);
            int stride = width * 4;
            var pixels = new byte[stride * height];
            GL.ReadPixels(0, 0, width, height, PixelFormat.Bgra, PixelType.UnsignedByte, pixels);

            // GL rows run bottom-up; images run top-down. Alpha is forced opaque (blending leaves it below 1).
            var flipped = new byte[pixels.Length];
            for (int row = 0; row < height; row++)
            {
                Array.Copy(pixels, (height - 1 - row) * stride, flipped, row * stride, stride);
            }

            for (int a = 3; a < flipped.Length; a += 4)
            {
                flipped[a] = 255;
            }

            return Result<byte[]>.Ok(flipped);
        }
        finally
        {
            GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer, prevRead);
            GL.BindFramebuffer(FramebufferTarget.DrawFramebuffer, prevDraw);
            GL.Viewport(prevViewport[0], prevViewport[1], prevViewport[2], prevViewport[3]);
            GL.BindRenderbuffer(RenderbufferTarget.Renderbuffer, 0);
            if (outColour != 0)
            {
                GL.DeleteRenderbuffer(outColour);
            }

            if (outFbo != 0)
            {
                GL.DeleteFramebuffer(outFbo);
            }

            if (msDepth != 0)
            {
                GL.DeleteRenderbuffer(msDepth);
            }

            if (msColour != 0)
            {
                GL.DeleteRenderbuffer(msColour);
            }

            if (msFbo != 0)
            {
                GL.DeleteFramebuffer(msFbo);
            }
        }
    }
}
