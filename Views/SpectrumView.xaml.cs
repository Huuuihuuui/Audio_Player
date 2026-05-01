// 频谱可视化控件 —— 64 根柱子，EMA 平滑过渡动画
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace MusicPlayer.Views;

public partial class SpectrumView : UserControl
{
    private const int BarCount = 64;
    private const float Smoothing = 0.45f;      // EMA 平滑（0=冻结, 1=无平滑）
    private const float DisplayGain = 1.5f;      // 显示增益：>1 整体抬高，<1 压暗
    private readonly Rectangle[] _bars = new Rectangle[BarCount];
    private readonly double[] _current = new double[BarCount];
    private bool _initialized;

    public SpectrumView()
    {
        InitializeComponent();

        for (int i = 0; i < BarCount; i++)
        {
            var rect = new Rectangle
            {
                Width = 2.5,
                Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#e94560")),
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
                test[i] = (i + 1) / (float)BarCount; // 0~1 测试值
            UpdateBars(test);
        };
    }

    // 外部调用：更新频谱柱高度（values 范围为 0~1），内部做 EMA 平滑并 ×80 转像素
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
