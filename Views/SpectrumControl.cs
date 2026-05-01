// 频谱可视化控件 —— 用 Canvas + Rectangle 绘制 FFT 频谱柱状图，白/亮色条
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace MusicPlayer.Views;

public class SpectrumControl : Control
{
    // 用于绘制的 Canvas
    private Canvas? _canvas;

    static SpectrumControl()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(SpectrumControl),
            new FrameworkPropertyMetadata(typeof(SpectrumControl)));
    }

    // FFT 采样数据，更新时自动重绘
    public static readonly DependencyProperty SamplesProperty =
        DependencyProperty.Register(nameof(Samples), typeof(float[]), typeof(SpectrumControl),
            new PropertyMetadata(null, OnSamplesChanged));

    public float[]? Samples
    {
        get => (float[]?)GetValue(SamplesProperty);
        set => SetValue(SamplesProperty, value);
    }

    private static void OnSamplesChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((SpectrumControl)d).Draw();
    }

    // 控件模板加载时获取 Canvas 引用
    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        _canvas = GetTemplateChild("PART_Canvas") as Canvas;
        Draw();
    }

    // 在 Canvas 上绘制频谱条
    private void Draw()
    {
        if (_canvas == null) return;

        var samples = Samples;
        if (samples == null || samples.Length == 0) return;

        var width = ActualWidth;
        var height = ActualHeight;

        if (width <= 0 || height <= 0)
        {
            width = _canvas.Width > 0 ? _canvas.Width : 800;
            height = _canvas.Height > 0 ? _canvas.Height : 30;
        }

        _canvas.Children.Clear();

        var barCount = Math.Min(samples.Length / 2, 64);
        var barWidth = Math.Max(1, width / barCount - 2);
        var brush = Brushes.White;

        for (int i = 0; i < barCount; i++)
        {
            var value = Math.Min(samples[i], 1.0);
            var barHeight = value * height;
            if (barHeight < 1) barHeight = 1;

            var rect = new Rectangle
            {
                Width = barWidth,
                Height = barHeight,
                Fill = brush,
                RadiusX = 1,
                RadiusY = 1
            };

            Canvas.SetLeft(rect, i * (barWidth + 2));
            Canvas.SetBottom(rect, 0);

            _canvas.Children.Add(rect);
        }
    }
}
