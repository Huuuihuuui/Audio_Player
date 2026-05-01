// 频谱柱状图单根柱子 —— 支持属性变更通知，使 WPF 绑定能自动更新高度
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace MusicPlayer.Models;

public class SpectrumBar : INotifyPropertyChanged
{
    private double _height;
    public double Height
    {
        get => _height;
        set
        {
            if (_height != value)
            {
                _height = value;
                OnPropertyChanged();
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
