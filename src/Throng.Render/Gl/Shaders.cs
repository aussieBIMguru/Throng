namespace Throng.Render.Gl;

/// <summary>GLSL 3.30 sources for the renderer's programs.</summary>
internal static class Shaders
{
    /// <summary>Shared vertex shader: position + per-vertex colour, with a global alpha.</summary>
    public const string FlatVertex = """
        #version 330 core
        layout(location = 0) in vec3 aPosition;
        layout(location = 1) in vec4 aColor;
        uniform mat4 uViewProjection;
        uniform float uAlpha;
        out vec4 vColor;
        void main()
        {
            vColor = vec4(aColor.rgb, aColor.a * uAlpha);
            gl_Position = uViewProjection * vec4(aPosition, 1.0);
        }
        """;

    /// <summary>Flat colour for triangles.</summary>
    public const string FlatFragment = """
        #version 330 core
        in vec4 vColor;
        out vec4 FragColor;
        void main()
        {
            FragColor = vColor;
        }
        """;

    /// <summary>
    /// Expands each GL_LINES segment into a screen-space quad <c>uLineWidth</c> pixels wide with square caps
    /// (D-049), since core-profile GL ignores glLineWidth above 1. Segments crossing the near plane are clipped in
    /// clip space first so the perspective divide stays valid.
    /// </summary>
    public const string LineGeometry = """
        #version 330 core
        layout(lines) in;
        layout(triangle_strip, max_vertices = 4) out;
        in vec4 vColor[];
        out vec4 gColor;
        uniform vec2 uViewport;
        uniform float uLineWidth;
        const float kNearW = 1e-4;
        void main()
        {
            vec4 p0 = gl_in[0].gl_Position;
            vec4 p1 = gl_in[1].gl_Position;
            vec4 c0 = vColor[0];
            vec4 c1 = vColor[1];

            if (p0.w < kNearW && p1.w < kNearW)
            {
                return;
            }

            if (p0.w < kNearW)
            {
                float t = (kNearW - p0.w) / (p1.w - p0.w);
                p0 = mix(p0, p1, t);
                c0 = mix(c0, c1, t);
            }
            else if (p1.w < kNearW)
            {
                float t = (kNearW - p1.w) / (p0.w - p1.w);
                p1 = mix(p1, p0, t);
                c1 = mix(c1, c0, t);
            }

            vec2 halfViewport = uViewport * 0.5;
            vec2 s0 = (p0.xy / p0.w) * halfViewport;
            vec2 s1 = (p1.xy / p1.w) * halfViewport;
            vec2 dir = s1 - s0;
            float len = length(dir);
            dir = len > 1e-5 ? dir / len : vec2(1.0, 0.0);

            float halfWidth = uLineWidth * 0.5;
            vec2 normal = vec2(-dir.y, dir.x) * halfWidth;
            vec2 cap = dir * halfWidth;

            // Back to clip space: divide the pixel offset by the half viewport, then multiply by w.
            vec2 n0 = normal / halfViewport * p0.w;
            vec2 n1 = normal / halfViewport * p1.w;
            vec2 e0 = cap / halfViewport * p0.w;
            vec2 e1 = cap / halfViewport * p1.w;

            gColor = c0; gl_Position = vec4(p0.xy - e0 + n0, p0.zw); EmitVertex();
            gColor = c0; gl_Position = vec4(p0.xy - e0 - n0, p0.zw); EmitVertex();
            gColor = c1; gl_Position = vec4(p1.xy + e1 + n1, p1.zw); EmitVertex();
            gColor = c1; gl_Position = vec4(p1.xy + e1 - n1, p1.zw); EmitVertex();
            EndPrimitive();
        }
        """;

    /// <summary>Textured quads for plan underlays (D-085): position + UV, global alpha.</summary>
    public const string TexturedVertex = """
        #version 330 core
        layout(location = 0) in vec3 aPosition;
        layout(location = 1) in vec2 aUv;
        uniform mat4 uViewProjection;
        out vec2 vUv;
        void main()
        {
            vUv = aUv;
            gl_Position = uViewProjection * vec4(aPosition, 1.0);
        }
        """;

    /// <summary>Samples the underlay texture with the underlay's opacity.</summary>
    public const string TexturedFragment = """
        #version 330 core
        in vec2 vUv;
        uniform sampler2D uTexture;
        uniform float uAlpha;
        out vec4 FragColor;
        void main()
        {
            vec4 c = texture(uTexture, vUv);
            FragColor = vec4(c.rgb, c.a * uAlpha);
        }
        """;

    /// <summary>Flat colour for the geometry-shader output.</summary>
    public const string LineFragment = """
        #version 330 core
        in vec4 gColor;
        out vec4 FragColor;
        void main()
        {
            FragColor = gColor;
        }
        """;

    /// <summary>
    /// Instanced agents (D-102): a unit capsule mesh (per vertex: position, shade, part) placed per instance by
    /// position + heading, coloured by profile and scaled by radius and height. Part 3 is the stuck ring (D-106),
    /// kept at floor height and blinking while the stuck flag is set.
    /// </summary>
    public const string AgentVertex = """
        #version 330 core
        layout(location = 0) in vec3 aPosition;
        layout(location = 1) in float aShade;
        layout(location = 2) in float aPart;
        layout(location = 3) in vec4 iPosHeading;
        layout(location = 4) in vec3 iColor;
        layout(location = 5) in vec3 iSize;
        uniform mat4 uViewProjection;
        uniform float uTime;
        out vec4 vColor;
        void main()
        {
            float c = cos(iPosHeading.w);
            float s = sin(iPosHeading.w);
            vec2 local = aPosition.xy * iSize.x;
            vec2 rotated = vec2(local.x * c - local.y * s, local.x * s + local.y * c);
            float z = aPart > 2.5 ? aPosition.z : aPosition.z * iSize.y;
            vec3 world = vec3(iPosHeading.xy + rotated, iPosHeading.z + z);

            vec3 rgb = iColor * aShade;
            float alpha = 1.0;
            if (aPart > 2.5)
            {
                rgb = vec3(1.0, 0.25, 0.2);
                alpha = iSize.z * (0.55 + 0.45 * sin(uTime * 6.0));
            }
            else if (aPart > 1.5)
            {
                rgb = mix(iColor, vec3(1.0), 0.6) * aShade;
            }

            vColor = vec4(rgb, alpha);
            gl_Position = uViewProjection * vec4(world, 1.0);
        }
        """;

    /// <summary>Agent fragments; fully transparent ones (the ring when not stuck) are discarded so they write no depth.</summary>
    public const string AgentFragment = """
        #version 330 core
        in vec4 vColor;
        out vec4 FragColor;
        void main()
        {
            if (vColor.a < 0.01)
            {
                discard;
            }

            FragColor = vColor;
        }
        """;
}
