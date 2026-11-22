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

    internal bool PillGlassEnabled => _modePillGlass is not null;

    private static readonly (string Pill, string Track)[] GlassSelectors =
    {
        ("ModeSelectionPill", "ModeSelectorTrack"),
        ("ThemeToggleThumb", "ThemeSelectorTrack"),
        ("DirectionSelectionPill", "DirectionSelectorTrack"),
        ("PdfPreviewSelectionPill", "PdfPreviewSelectorTrack"),
        ("OcrPreviewSelectionPill", "OcrPreviewSelectorTrack")
    };

    private void InitializePillGlass()
    {
        if (Resources["AppBackgroundBrush"] is not SolidColorBrush background ||
            Resources["ModeTrackBrush"] is not SolidColorBrush trackColor ||
            Resources["ModePillBrush"] is not SolidColorBrush pillColor)
            return;

        if (ModePillGlass.Compile(Program.GlassTrial) is not { } effect)
            return;

        var lenses = new List<ModePillGlass>();
        foreach (var (pillName, trackName) in GlassSelectors)
        {
            var pill = Required<Border>(pillName);
            if (pill.RenderTransform is not TranslateTransform translation)
                continue;
            var lens = new ModePillGlass(Required<Border>(trackName), pill, translation,
                background, trackColor, pillColor, effect);
            lenses.Add(lens);
            if (pillName == "ModeSelectionPill")
                _modePillGlass = lens;
        }
        Closed += (_, _) =>
        {
            foreach (var lens in lenses)
                lens.Dispose();
            effect.Dispose();
        };
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
        private readonly GlassDecoration _pillDecoration;
        private int _disabled;
        private int _rendered;
        private bool _disposed;

        public bool IsAvailable => Volatile.Read(ref _disabled) == 0;
        public bool HasRendered => Volatile.Read(ref _rendered) != 0;

        public ModePillGlass(Border track, Border pill, TranslateTransform translation,
            SolidColorBrush background, SolidColorBrush trackColor, SolidColorBrush pillColor, SKRuntimeEffect effect)
        {
            _track = track;
            _pill = pill;
            _translation = translation;
            _background = background;
            _trackColor = trackColor;
            _pillColor = pillColor;
            _effect = effect;
            _pillDecoration = new GlassDecoration(this);

            _pill.Child = _pillDecoration;
            _track.PropertyChanged += OnBoundsChanged;
            _pill.PropertyChanged += OnBoundsChanged;
            _translation.Changed += OnTranslationChanged;
            _background.PropertyChanged += OnPaletteChanged;
            _trackColor.PropertyChanged += OnPaletteChanged;
            _pillColor.PropertyChanged += OnPaletteChanged;
        }

        public static SKRuntimeEffect? Compile(ModePillGlassTrial trial)
        {
            try
            {
                var source = trial == ModePillGlassTrial.SimulateFailure ? "invalid SkSL" : ShaderSource;
                var effect = SKRuntimeEffect.CreateShader(source, out var error);
                if (effect is not null)
                    return effect;

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
                _pillDecoration.InvalidateVisual();
        }

        private void OnPaletteChanged(object? sender, AvaloniaPropertyChangedEventArgs args)
        {
            if (args.Property == SolidColorBrush.ColorProperty)
                _pillDecoration.InvalidateVisual();
        }

        private void OnTranslationChanged(object? sender, EventArgs args) => _pillDecoration.InvalidateVisual();

        private void Render(DrawingContext context, Size size)
        {
            if (Volatile.Read(ref _disabled) != 0 || size.Width <= 0 || size.Height <= 0)
                return;

            var trackBounds = _track.Bounds;
            if (trackBounds.Width <= 0 || trackBounds.Height <= 0)
                return;

            var origin = _pill.Bounds.Position
                + new Vector(_pill.BorderThickness.Left + _translation.X, _pill.BorderThickness.Top)
                - trackBounds.Position;
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
                    ["pillColor"] = ToRgb(_pillColor.Color)
                };
                var shader = _effect.ToShader(uniforms);
                if (shader is null)
                {
                    FallBack();
                    return;
                }
                // The recorded draw owns its shader until Avalonia releases the operation.
                context.Custom(new GlassDrawOperation(this, new Rect(size), shader));
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
            _pill.Child = null;
        }

        private sealed class GlassDecoration(ModePillGlass owner) : Control
        {
            protected override void OnInitialized()
            {
                base.OnInitialized();
                IsHitTestVisible = false;
                Focusable = false;
            }

            public override void Render(DrawingContext context) => owner.Render(context, Bounds.Size);
        }

        private sealed class GlassDrawOperation(ModePillGlass owner, Rect bounds, SKShader shader)
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
                    Volatile.Write(ref owner._rendered, 1);
                }
                catch (Exception error) when (IsDrawingFailure(error))
                {
                    owner.FallBack(error);
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

            float2 fromCapsuleAxis(float2 point, float2 size) {
                float2 p = point - size * 0.5;
                float span = max(0.0, (size.x - size.y) * 0.5);
                return p - float2(clamp(p.x, -span, span), 0.0);
            }

            float trackDistance(float2 p) {
                return length(fromCapsuleAxis(p, trackSize)) - trackSize.y * 0.5;
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
                float distance = radial - localSize.y * 0.5;
                float mask = coverage(distance);
                if (mask <= 0.0) return half4(0.0);

                float2 outward = perpendicular / max(radial, 0.01);
                float bevel = clamp(1.0 + distance / 13.0, 0.0, 1.0);
                float slope = 1.55 * pow(bevel, 1.28);
                float3 normal = normalize(float3(outward * slope, 1.0));
                float3 ray = refract(float3(0.0, 0.0, -1.0), normal, 1.0 / 1.47);
                // Back-project the ray across the thickness of the rounded bevel.
                float2 offset = -ray.xy * (13.0 / max(-ray.z, 0.2));

                float2 samplePoint = point + origin + offset;
                float3 backdrop = trackSurface(samplePoint);
                // Scatter a little of the surrounding color while keeping the lensing at the rim.
                float2 spread = outward * 1.45;
                backdrop = mix(backdrop, (trackSurface(samplePoint + spread) +
                    trackSurface(samplePoint - spread)) * 0.5, 0.27);
                float3 color = mix(backdrop, pillColor, 0.37);

                float light = dot(outward, normalize(float2(-0.53, -0.85)));
                float glint = exp(-abs(distance + 1.5) * 0.85);
                float reflection = glint * (0.06 + 0.53 * pow(max(light, 0.0), 6.0)
                    + 0.22 * pow(max(-light, 0.0), 11.0));
                float shade = exp(-abs(distance + 0.45) * 1.45) * 0.18;
                color = mix(color, float3(0.0), shade);
                color = mix(color, float3(1.0), reflection);
                return half4(color * mask, mask);
            }
            """;
    }
}
