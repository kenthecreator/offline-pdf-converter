using Avalonia;
using Avalonia.Media;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using SkiaSharp;

namespace OfflinePDFConverter.Views;

internal sealed class GlassOptics(SKRuntimeEffect effect, SolidColorBrush background) : IDisposable
{
    private bool _disposed;

    internal SolidColorBrush Background { get; } = background;

    public static GlassOptics? Compile(ModePillGlassTrial trial, SolidColorBrush background)
    {
        try
        {
            var source = trial == ModePillGlassTrial.SimulateFailure ? "invalid SkSL" : ShaderSource;
            if (SKRuntimeEffect.CreateShader(source, out var error) is { } compiled)
                return new GlassOptics(compiled, background);

            System.Diagnostics.Trace.WriteLine($"Glass shader compilation failed: {error}");
        }
        catch (Exception error) when (IsDrawingFailure(error))
        {
            System.Diagnostics.Trace.WriteLine($"Glass shader initialization failed: {error.Message}");
        }
        return null;
    }

    public bool Draw(DrawingContext context, Size size, Size trackSize, Vector origin,
        Color surround, Color track, Color surface, bool rimOnly, Action<bool> onDraw, CornerRadius? cornerRadius = null)
    {
        if (_disposed)
            return false;

        try
        {
            float[] radii = cornerRadius is { } radius
                ? [ClampRadius(radius.TopLeft), ClampRadius(radius.TopRight),
                    ClampRadius(radius.BottomRight), ClampRadius(radius.BottomLeft)]
                : [0, 0, 0, 0];
            float ClampRadius(double value) => (float)Math.Clamp(value, 0, Math.Min(size.Width, size.Height) / 2);
            using var uniforms = new SKRuntimeEffectUniforms(effect)
            {
                ["trackSize"] = new[] { (float)trackSize.Width, (float)trackSize.Height },
                ["localSize"] = new[] { (float)size.Width, (float)size.Height },
                ["origin"] = new[] { (float)origin.X, (float)origin.Y },
                ["surround"] = Rgb(surround),
                ["trackColor"] = new[] { track.R / 255f, track.G / 255f, track.B / 255f, track.A / 255f },
                ["pillColor"] = Rgb(surface),
                ["rimOnly"] = rimOnly ? 1f : 0f,
                ["roundedRectangle"] = cornerRadius.HasValue ? 1f : 0f,
                ["localRadii"] = radii
            };
            if (effect.ToShader(uniforms) is not { } shader)
                return false;

            var operation = new DrawOperation(new Rect(size), shader, onDraw);
            try
            {
                context.Custom(operation);
            }
            catch
            {
                operation.Dispose();
                throw;
            }
            return true;
        }
        catch (Exception error) when (IsDrawingFailure(error))
        {
            System.Diagnostics.Trace.WriteLine($"Glass drawing unavailable: {error.Message}");
            return false;
        }
    }

    private static float[] Rgb(Color color) => new[] { color.R / 255f, color.G / 255f, color.B / 255f };

    private static bool IsDrawingFailure(Exception error) =>
        error is InvalidOperationException or ArgumentException or NotSupportedException
            or DllNotFoundException or EntryPointNotFoundException or TypeInitializationException;

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        effect.Dispose();
    }

    private sealed class DrawOperation(Rect bounds, SKShader shader, Action<bool> onDraw) : ICustomDrawOperation
    {
        public Rect Bounds => bounds;

        public bool HitTest(Point point) => false;

        public bool Equals(ICustomDrawOperation? other) => ReferenceEquals(this, other);

        public void Render(ImmediateDrawingContext context)
        {
            try
            {
                var feature = context.TryGetFeature(typeof(ISkiaSharpApiLeaseFeature)) as ISkiaSharpApiLeaseFeature;
                using var lease = feature?.Lease();
                if (lease is null)
                {
                    onDraw(false);
                    return;
                }

                using var paint = new SKPaint { Shader = shader };
                lease.SkCanvas.DrawRect(SKRect.Create((float)bounds.Width, (float)bounds.Height), paint);
                onDraw(true);
            }
            catch (Exception error) when (IsDrawingFailure(error))
            {
                System.Diagnostics.Trace.WriteLine($"Glass drawing unavailable: {error.Message}");
                onDraw(false);
            }
        }

        public void Dispose() => shader.Dispose();
    }

    // Evaluate the existing solid track over the header at the refracted position.
    private const string ShaderSource = """
        uniform float2 trackSize;
        uniform float2 localSize;
        uniform float2 origin;
        uniform float3 surround;
        uniform float4 trackColor;
        uniform float3 pillColor;
        uniform float rimOnly;
        uniform float roundedRectangle;
        uniform float4 localRadii;

        float2 fromCapsuleAxis(float2 point, float2 size) {
            float2 p = point - size * 0.5;
            float2 span = max(float2(0.0), (size - size.yx) * 0.5);
            return p - clamp(p, -span, span);
        }

        float2 fromRoundedRectCorner(float2 centered, float2 size, out float radius) {
            radius = centered.y < 0.0
                ? (centered.x < 0.0 ? localRadii.x : localRadii.y)
                : (centered.x < 0.0 ? localRadii.w : localRadii.z);
            return abs(centered) - (size * 0.5 - radius);
        }

        float trackDistance(float2 p) {
            if (roundedRectangle > 0.5) {
                float radius;
                float2 edge = fromRoundedRectCorner(p - trackSize * 0.5, trackSize, radius);
                return length(max(edge, 0.0)) + min(max(edge.x, edge.y), 0.0) - radius;
            }
            return length(fromCapsuleAxis(p, trackSize)) - min(trackSize.x, trackSize.y) * 0.5;
        }

        float3 trackSurface(float2 p) {
            float inside = 1.0 - smoothstep(-0.7, 0.7, trackDistance(p));
            return mix(surround, trackColor.rgb, trackColor.a * inside);
        }

        float coverage(float distance) {
            return 1.0 - smoothstep(-0.65, 0.35, distance);
        }

        half4 main(float2 point) {
            float2 perpendicular = fromCapsuleAxis(point, localSize);
            float radial = length(perpendicular);
            float diameter = min(localSize.x, localSize.y);
            float distance = radial - diameter * 0.5;
            float2 outward = perpendicular / max(radial, 0.01);
            if (roundedRectangle > 0.5) {
                float2 centered = point - localSize * 0.5;
                float radius;
                float2 edge = fromRoundedRectCorner(centered, localSize, radius);
                float2 outside = max(edge, 0.0);
                float outsideLength = length(outside);
                distance = outsideLength + min(max(edge.x, edge.y), 0.0) - radius;
                float2 direction = outsideLength > 0.0 ? outside / max(outsideLength, 0.001)
                    : (edge.x > edge.y ? float2(1.0, 0.0) : float2(0.0, 1.0));
                outward = direction * sign(centered);
            }
            float mask = coverage(distance);
            // The command-button lens is above the presenter. Its center passes the label through.
            if (rimOnly > 0.5)
                mask *= coverage(distance + 0.7) * smoothstep(-min(8.0, diameter * 0.34), -min(3.5, diameter * 0.15), distance);
            if (mask <= 0.0) return half4(0.0);

            float bevel = clamp(1.0 + distance / min(13.0, diameter * 0.37), 0.0, 1.0);
            float slope = 1.55 * pow(bevel, 1.28);
            float3 normal = normalize(float3(outward * slope, 1.0));
            float3 ray = refract(float3(0.0, 0.0, -1.0), normal, 1.0 / 1.47);
            float lightSurface = smoothstep(0.48, 0.82, surround.r);
            // Back-project the ray across the thickness of the rounded bevel.
            float2 offset = -ray.xy * (mix(13.0, 17.0, lightSurface) / max(-ray.z, 0.2));

            float2 samplePoint = point + origin + offset;
            float3 backdrop = trackSurface(samplePoint);
            // Scatter a little of the surrounding color while keeping the lensing at the rim.
            float2 spread = outward * 1.45;
            backdrop = mix(backdrop, (trackSurface(samplePoint + spread) +
                trackSurface(samplePoint - spread)) * 0.5, 0.27);
            float light = dot(outward, normalize(float2(-0.53, -0.85)));
            float glint = exp(-abs(distance + mix(1.5, 2.05, lightSurface)) * 0.85);
            float reflection = glint * (mix(0.06, 0.12, lightSurface) +
                mix(0.53, 0.78, lightSurface) * pow(max(light, 0.0), 6.0)
                + 0.22 * pow(max(-light, 0.0), 11.0));
            float shade = exp(-abs(distance + 0.45) * 1.45) * mix(0.18, 0.34, lightSurface);
            shade += lightSurface * exp(-abs(distance + 3.6) * 0.70)
                * (0.027 + 0.073 * max(-light, 0.0));

            float3 color = mix(backdrop, pillColor, mix(0.37, 0.18, lightSurface));
            color = mix(color, float3(0.0), shade);
            color = mix(color, float3(1.0), reflection);
            if (rimOnly > 0.5) {
                // Match the pill's reflected tone over the existing fill. On white surfaces
                // the bright glint needs a soft neighboring shadow to read as curved glass.
                float3 luminance = float3(0.2126, 0.7152, 0.0722);
                float bentLight = dot(backdrop - pillColor, luminance);
                float darkResponse = reflection * 0.73 - shade * 0.77 + bentLight * 0.13;
                float shoulder = exp(-abs(distance + 2.8) * 0.62);
                float direction = pow(max(-light, 0.0), 2.0);
                float paleFace = smoothstep(0.58, 0.9, dot(pillColor, luminance));
                float target = dot(color - pillColor, luminance) - shoulder * direction * paleFace * 0.045;
                float tone = dot(pillColor, luminance);
                float lightResponse = target / max(target > 0.0 ? 1.0 - tone : tone, 0.15);
                float response = mix(darkResponse, lightResponse, lightSurface);
                float alpha = mask * min(abs(response), mix(0.36, 0.22, lightSurface));
                return half4(float3(response > 0.0 ? alpha : 0.0), alpha);
            }

            return half4(color * mask, mask);
        }
        """;
}
