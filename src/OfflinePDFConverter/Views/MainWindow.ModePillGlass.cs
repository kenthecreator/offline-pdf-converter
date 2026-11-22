using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using Avalonia.Threading;
using SkiaSharp;

namespace OfflinePDFConverter.Views;

public partial class MainWindow
{
    private ModePillGlass? _modePillGlass;

    private void InitializeModePillGlass(Border pill)
    {
        if (Resources["AppBackgroundBrush"] is not SolidColorBrush background ||
            Resources["ModeTrackBrush"] is not SolidColorBrush trackColor ||
            Resources["ModePillBrush"] is not SolidColorBrush pillColor)
            return;

        var track = Required<Border>("ModeSelectorTrack");
        _modePillGlass = ModePillGlass.TryCreate(
            track, pill, _modeSelectionTransform, background, trackColor, pillColor, Program.GlassTrial);
        if (_modePillGlass is { } glass)
            Closed += (_, _) => glass.Dispose();
    }

    private sealed class ModePillGlass : IDisposable
    {
        private readonly Border _track;
        private readonly Border _pill;
        private readonly TranslateTransform _translation;
        private readonly SolidColorBrush _background;
        private readonly SolidColorBrush _trackColor;
        private readonly SolidColorBrush _pillColor;
        private readonly SKRuntimeEffect _effect;
        private readonly GlassDecoration _trackDecoration;
        private readonly GlassDecoration _pillDecoration;
        private int _disabled;
        private int _rendered;
        private bool _disposed;

        public bool IsAvailable => Volatile.Read(ref _disabled) == 0;
        public bool HasRendered => Volatile.Read(ref _rendered) != 0;

        private ModePillGlass(Border track, Border pill, TranslateTransform translation,
            SolidColorBrush background, SolidColorBrush trackColor, SolidColorBrush pillColor, SKRuntimeEffect effect)
        {
            _track = track;
            _pill = pill;
            _translation = translation;
            _background = background;
            _trackColor = trackColor;
            _pillColor = pillColor;
            _effect = effect;
            _trackDecoration = new GlassDecoration(this, false);
            _pillDecoration = new GlassDecoration(this, true);

            _track.Child = _trackDecoration;
            _pill.Child = _pillDecoration;
            _track.PropertyChanged += OnBoundsChanged;
            _pill.PropertyChanged += OnBoundsChanged;
            _translation.Changed += OnTranslationChanged;
            _background.PropertyChanged += OnPaletteChanged;
            _trackColor.PropertyChanged += OnPaletteChanged;
            _pillColor.PropertyChanged += OnPaletteChanged;
        }

        public static ModePillGlass? TryCreate(Border track, Border pill, TranslateTransform translation,
            SolidColorBrush background, SolidColorBrush trackColor, SolidColorBrush pillColor, ModePillGlassTrial trial)
        {
            try
            {
                var source = trial == ModePillGlassTrial.SimulateFailure ? "invalid SkSL" : ShaderSource;
                var effect = SKRuntimeEffect.CreateShader(source, out var error);
                if (effect is not null)
                    return new ModePillGlass(track, pill, translation, background, trackColor, pillColor, effect);

                System.Diagnostics.Trace.WriteLine($"Mode pill shader compilation failed: {error}");
            }
            catch (Exception error) when (IsDrawingFailure(error))
            {
                System.Diagnostics.Trace.WriteLine($"Mode pill shader initialization failed: {error.Message}");
            }
            return null;
        }

        private void OnBoundsChanged(object? sender, AvaloniaPropertyChangedEventArgs args)
        {
            if (args.Property == BoundsProperty)
                InvalidateDecorations();
        }

        private void OnPaletteChanged(object? sender, AvaloniaPropertyChangedEventArgs args)
        {
            if (args.Property == SolidColorBrush.ColorProperty)
                InvalidateDecorations();
        }

        private void OnTranslationChanged(object? sender, EventArgs args) => _pillDecoration.InvalidateVisual();

        private void InvalidateDecorations()
        {
            _trackDecoration.InvalidateVisual();
            _pillDecoration.InvalidateVisual();
        }

        private void Render(DrawingContext context, Size size, bool drawPill)
        {
            if (Volatile.Read(ref _disabled) != 0 || size.Width <= 0 || size.Height <= 0)
                return;

            var trackBounds = _track.Bounds;
            if (trackBounds.Width <= 0 || trackBounds.Height <= 0)
                return;

            var origin = drawPill
                ? _pill.Bounds.Position + new Vector(_pill.BorderThickness.Left + _translation.X, _pill.BorderThickness.Top)
                  - trackBounds.Position
                : default;
            var track = _trackColor.Color;
            try
            {
                using var uniforms = new SKRuntimeEffectUniforms(_effect)
                {
                    ["trackSize"] = new[] { (float)trackBounds.Width, (float)trackBounds.Height },
                    ["localSize"] = new[] { (float)size.Width, (float)size.Height },
                    ["origin"] = new[] { (float)origin.X, (float)origin.Y },
                    ["surround"] = ToRgb(_background.Color),
                    ["trackColor"] = new[] { track.R / 255f, track.G / 255f, track.B / 255f, track.A / 255f },
                    ["pillColor"] = ToRgb(_pillColor.Color),
                    ["drawPill"] = drawPill ? 1f : 0f
                };
                var shader = _effect.ToShader(uniforms);
                if (shader is null)
                {
                    FallBack();
                    return;
                }
                // The recorded draw owns its shader until Avalonia releases the operation.
                context.Custom(new GlassDrawOperation(this, new Rect(size), shader, drawPill));
            }
            catch (Exception error) when (IsDrawingFailure(error))
            {
                FallBack(error);
            }
        }

        private static float[] ToRgb(Color color) => new[] { color.R / 255f, color.G / 255f, color.B / 255f };

        private static bool IsDrawingFailure(Exception error) =>
            error is InvalidOperationException or ArgumentException or NotSupportedException
                or DllNotFoundException or EntryPointNotFoundException or TypeInitializationException;

        private void FallBack(Exception? error = null)
        {
            if (Interlocked.Exchange(ref _disabled, 1) != 0)
                return;

            System.Diagnostics.Trace.WriteLine($"Mode pill refraction unavailable: {error?.Message ?? "Skia drawing unavailable"}");
            Dispatcher.UIThread.Post(Dispose);
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            Interlocked.Exchange(ref _disabled, 1);
            _track.PropertyChanged -= OnBoundsChanged;
            _pill.PropertyChanged -= OnBoundsChanged;
            _translation.Changed -= OnTranslationChanged;
            _background.PropertyChanged -= OnPaletteChanged;
            _trackColor.PropertyChanged -= OnPaletteChanged;
            _pillColor.PropertyChanged -= OnPaletteChanged;
            _track.Child = null;
            _pill.Child = null;
            _effect.Dispose();
        }

        private sealed class GlassDecoration(ModePillGlass owner, bool drawPill) : Control
        {
            protected override void OnInitialized()
            {
                base.OnInitialized();
                IsHitTestVisible = false;
                Focusable = false;
            }

            public override void Render(DrawingContext context) => owner.Render(context, Bounds.Size, drawPill);
        }

        private sealed class GlassDrawOperation(ModePillGlass owner, Rect bounds, SKShader shader, bool drawPill)
            : ICustomDrawOperation
        {
            public Rect Bounds => bounds;

            public bool HitTest(Point point) => false;

            public bool Equals(ICustomDrawOperation? other) => ReferenceEquals(this, other);

            public void Render(ImmediateDrawingContext context)
            {
                if (Volatile.Read(ref owner._disabled) != 0)
                    return;
                try
                {
                    var feature = context.TryGetFeature(typeof(ISkiaSharpApiLeaseFeature)) as ISkiaSharpApiLeaseFeature;
                    using var lease = feature?.Lease();
                    if (lease is null)
                    {
                        owner.FallBack();
                        return;
                    }
                    using var paint = new SKPaint { Shader = shader };
                    lease.SkCanvas.DrawRect(SKRect.Create((float)bounds.Width, (float)bounds.Height), paint);
                    if (drawPill)
                        Volatile.Write(ref owner._rendered, 1);
                }
                catch (Exception error) when (IsDrawingFailure(error))
                {
                    owner.FallBack(error);
                }
            }

            public void Dispose() => shader.Dispose();
        }

        // Both layers use the same procedural track colors. No window or control pixels are read.
        private const string ShaderSource = """
            uniform float2 trackSize;
            uniform float2 localSize;
            uniform float2 origin;
            uniform float3 surround;
            uniform float4 trackColor;
            uniform float3 pillColor;
            uniform float drawPill;

            float2 fromCapsuleAxis(float2 point, float2 size) {
                float2 p = point - size * 0.5;
                float span = max(0.0, (size.x - size.y) * 0.5);
                return p - float2(clamp(p.x, -span, span), 0.0);
            }

            float trackDistance(float2 p) {
                return length(fromCapsuleAxis(p, trackSize)) - trackSize.y * 0.5;
            }

            float3 trackSurface(float2 p) {
                float dark = 1.0 - smoothstep(0.2, 0.75, surround.r);
                float3 base = mix(surround, trackColor.rgb, trackColor.a);
                float3 blue = mix(float3(0.38, 0.60, 0.79), float3(0.30, 0.50, 0.72), dark);
                float band = 0.5 + 0.5 * cos((p.x + 0.68 * p.y - 10.0) * 0.077);
                float sheen = 0.023 + 0.14 * pow(band, 7.0);
                float3 color = mix(base, blue, sheen);
                float softEdge = smoothstep(-5.0, 0.2, trackDistance(p));
                return mix(color, surround, softEdge);
            }

            float coverage(float distance) {
                return 1.0 - smoothstep(-0.65, 0.35, distance);
            }

            half4 main(float2 point) {
                if (drawPill < 0.5) {
                    float alpha = coverage(trackDistance(point));
                    return half4(trackSurface(point) * alpha, alpha);
                }

                float2 perpendicular = fromCapsuleAxis(point, localSize);
                float radial = length(perpendicular);
                float distance = radial - localSize.y * 0.5;
                float mask = coverage(distance);
                if (mask <= 0.0) return half4(0.0);

                float2 outward = perpendicular / max(radial, 0.01);
                float bevel = clamp(1.0 + distance / 13.0, 0.0, 1.0);
                float slope = 1.55 * pow(bevel, 1.28);
                float3 normal = normalize(float3(outward * slope, 1.0));
                float3 ray = refract(float3(0.0, 0.0, -1.0), normal, 1.0 / 1.47);
                float2 offset = ray.xy * (19.0 / max(-ray.z, 0.2));

                float2 samplePoint = point + origin + offset;
                float3 backdrop = trackSurface(samplePoint);
                // Scatter a little of the surrounding color while keeping the lensing at the rim.
                backdrop = mix(backdrop, (trackSurface(samplePoint + float2(1.5, 0.7)) +
                    trackSurface(samplePoint - float2(1.5, 0.7))) * 0.5, 0.23);
                float3 color = mix(backdrop, pillColor, 0.40);

                float light = dot(outward, normalize(float2(-0.53, -0.85)));
                float glint = exp(-abs(distance + 1.8) * 0.91);
                float reflection = glint * (0.045 + 0.56 * pow(max(light, 0.0), 8.0)
                    + 0.29 * pow(max(-light, 0.0), 16.0));
                float shade = exp(-abs(distance + 0.5) * 1.35) * 0.21;
                color = mix(color, float3(0.025, 0.035, 0.055), shade);
                color = mix(color, float3(0.84, 0.93, 1.0), reflection);
                return half4(color * mask, mask);
            }
            """;
    }
}
