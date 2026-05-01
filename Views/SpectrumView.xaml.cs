// 频谱可视化控件 —— 64 根柱子，多色渐变 + EMA 平滑过渡
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace MusicPlayer.Views;

public partial class SpectrumView : UserControl
{
    private const int BarCount = 64;
    private const float Smoothing = 0.45f;
    private const float DisplayGain = 1.5f;
    private readonly Rectangle[] _bars = new Rectangle[BarCount];
    private readonly double[] _current = new double[BarCount];
    private bool _initialized;

    // 预计算的频谱柱颜色数组：低频红 → 中频亮红 → 高频金
    private static readonly Color[] BarColors = BuildColorGradient();

    private static Color[] BuildColorGradient()
    {
        var colors = new Color[BarCount];
        var low = Color.FromRgb(0x5B, 0x5B, 0x5B);   // #5b5b5b
        var mid = Color.FromRgb(0x91, 0x91, 0x91);   // #919191
        var high = Color.FromRgb(0xE6, 0xE6, 0xE6);  // #e6e6e6

        for (int i = 0; i < BarCount; i++)
        {
            float t = i / (float)(BarCount - 1);
            if (t < 0.5f)
                colors[i] = LerpColor(low, mid, t / 0.5f);
            else
                colors[i] = LerpColor(mid, high, (t - 0.5f) / 0.5f);
        }
        return colors;
    }

    private static Color LerpColor(Color a, Color b, float t)
    {
        return Color.FromRgb(
            (byte)(a.R + (b.R - a.R) * t),
            (byte)(a.G + (b.G - a.G) * t),
            (byte)(a.B + (b.B - a.B) * t));
    }

    public SpectrumView()
    {
        InitializeComponent();

        for (int i = 0; i < BarCount; i++)
        {
            var rect = new Rectangle
            {
                Width = 2.5,
                RadiusX = 2,
                RadiusY = 2,
                Fill = new SolidColorBrush(BarColors[i]),
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0.5, 0, 0.5, 0),
                Height = 0.5
            };
            BarsPanel.Children.Add(rect);
            _bars[i] = rect;
            _current[i] = 0.5;
        }

        _initialized = true;

        Loaded += (_, _) =>
        {
            var test = new float[BarCount];
            for (int i = 0; i < BarCount; i++)
                test[i] = (i + 1) / (float)BarCount;
            UpdateBars(test);
        };
    }

    public void UpdateBars(float[] values)
    {
        if (!_initialized || values == null) return;

        var count = Math.Min(values.Length, BarCount);
        for (int i = 0; i < BarCount; i++)
        {
            var target = i < count ? Math.Clamp(values[i], 0, 1) : 0f;
            _current[i] = _current[i] * (1 - Smoothing) + target * Smoothing;
            _bars[i].Height = Math.Max(0.5, _current[i] * 80 * DisplayGain);
        }
    }
}
