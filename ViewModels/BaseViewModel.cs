// MVVM 基础设施 —— 所有 ViewModel 的基类，封装 INotifyPropertyChanged 接口
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace MusicPlayer.ViewModels;

public abstract class BaseViewModel : INotifyPropertyChanged
{
    // 属性变更通知事件（WPF 数据绑定核心）
    public event PropertyChangedEventHandler? PropertyChanged;

    // 手动触发属性变更通知
    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    // 设置属性值并自动通知 —— 只有值真的变化时才通知，避免无意义刷新
    protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }
}
