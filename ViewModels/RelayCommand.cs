// MVVM 基础设施 —— ICommand 实现，将 View 的按钮/菜单绑定到 ViewModel 的方法
using System.Windows.Input;

namespace MusicPlayer.ViewModels;

// 无参命令
public class RelayCommand : ICommand
{
    private readonly Action _execute;
    private readonly Func<bool>? _canExecute;

    public RelayCommand(Action execute, Func<bool>? canExecute = null)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _canExecute = canExecute;
    }

    // 当 CanExecute 结果变化时通知 UI 刷新按钮状态
    public event EventHandler? CanExecuteChanged
    {
        add { CommandManager.RequerySuggested += value; }
        remove { CommandManager.RequerySuggested -= value; }
    }

    // 判断命令是否可执行
    public bool CanExecute(object? parameter) => _canExecute?.Invoke() ?? true;

    // 执行命令
    public void Execute(object? parameter) => _execute();
}

// 带参数的命令
public class RelayCommand<T> : ICommand
{
    private readonly Action<T?> _execute;
    private readonly Func<T?, bool>? _canExecute;

    public RelayCommand(Action<T?> execute, Func<T?, bool>? canExecute = null)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _canExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged
    {
        add { CommandManager.RequerySuggested += value; }
        remove { CommandManager.RequerySuggested -= value; }
    }

    public bool CanExecute(object? parameter) => _canExecute?.Invoke((T?)parameter) ?? true;
    public void Execute(object? parameter) => _execute((T?)parameter);
}

// 异步无参命令 —— 支持 async/await 方法，执行期间自动禁用按钮防重复点击
public class AsyncRelayCommand : ICommand
{
    private readonly Func<Task> _execute;
    private readonly Func<bool>? _canExecute;
    private bool _isExecuting;   // 防止异步重入

    public AsyncRelayCommand(Func<Task> execute, Func<bool>? canExecute = null)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _canExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged
    {
        add { CommandManager.RequerySuggested += value; }
        remove { CommandManager.RequerySuggested -= value; }
    }

    // 执行中时不可再次执行
    public bool CanExecute(object? parameter) => !_isExecuting && (_canExecute?.Invoke() ?? true);

    // async void 执行：捕获异常但不阻塞 UI
    public async void Execute(object? parameter)
    {
        if (_isExecuting) return;
        _isExecuting = true;
        CommandManager.InvalidateRequerySuggested();
        try
        {
            await _execute();
        }
        finally
        {
            _isExecuting = false;
            CommandManager.InvalidateRequerySuggested();
        }
    }
}
