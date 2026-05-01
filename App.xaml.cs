// 应用程序入口 —— 启动时自动执行数据库迁移，并注册全局异常处理防止闪退
using System.Windows;
using System.Windows.Threading;
using Microsoft.EntityFrameworkCore;
using MusicPlayer.Data;

namespace MusicPlayer;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // 全局异常捕获：UI 线程未处理异常不闪退，弹出提示
        DispatcherUnhandledException += (_, args) =>
        {
            MessageBox.Show($"发生错误: {args.Exception.Message}", "错误",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            args.Handled = true; // 阻止应用退出
        };

        // 后台 Task 未捕获异常也不闪退
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            args.SetObserved();
        };

        using var db = new MusicDbContext();
        db.Database.Migrate();
    }
}
