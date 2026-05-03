// 主窗口代码后置 —— 处理导航、右键菜单、频谱歌词联动
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using MusicPlayer.Models;
using MusicPlayer.ViewModels;

namespace MusicPlayer;

public partial class MainWindow : Window
{
    private MainViewModel VM => (MainViewModel)DataContext;

    public MainWindow()
    {
        InitializeComponent();

        // 频谱数据 → SpectrumView
        VM.SpectrumUpdated += samples =>
        {
            var bars = new float[64];
            for (int i = 0; i < 64 && i < samples.Length; i++)
                bars[i] = Math.Clamp(samples[i], 0, 1);
            SpectrumViewControl.UpdateBars(bars);
        };

        // 歌词变化 → 平滑滚动到当前行
        VM.CurrentLyricChanged += idx =>
        {
            // 用低优先级延迟执行，确保布局完成（Seek 后尤其重要）
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (idx >= 0 && LyricsItemsControl.Items.Count > 0)
                {
                    SmoothScrollToLyric(idx);
                }
            }), System.Windows.Threading.DispatcherPriority.Background);
        };
    }

    // 队列移除歌曲
    private void QueueRemove_Click(object sender, RoutedEventArgs e)
    {
        if (QueueListBox.SelectedItem is Song song)
            VM.RemoveFromQueue(song);
    }

    // 队列中双击播放
    private void QueueList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is ListBox lb && lb.SelectedItem is Song song)
            VM.PlaySong(song);
    }

    // 双击歌曲直接播放
    private void SongList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is ListBox listBox && listBox.SelectedItem is Song song)
            VM.PlaySong(song);
    }

    // 阻止右键选中导航项
    private void NavListBox_PreviewRightDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
    }

    // 左侧导航栏切换（仅左键触发，右键不跳转）
    private async void NavListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (VM == null || Mouse.RightButton == MouseButtonState.Pressed) return;
        var item = NavListBox.SelectedItem as ListBoxItem;
        if (item?.Tag is not string tag) return;
        switch (tag)
        {
            case "all":
                VM.SearchText = string.Empty;
                VM.SelectedPlaylist = null;
                VM.CurrentView = "all";
                await VM.SwitchToAllSongs();
                break;
            case "favorites":
                VM.SearchText = string.Empty;
                VM.SelectedPlaylist = null;
                VM.CurrentView = "favorites";
                break;
            case "recent":
                VM.SearchText = string.Empty;
                VM.SelectedPlaylist = null;
                VM.CurrentView = "recent";
                break;
        }
    }

    // 歌单右键菜单关闭后取消高亮
    private void PlaylistContextMenu_Closed(object sender, RoutedEventArgs e)
    {
        VM.SelectedPlaylist = null;
    }

    // 歌单选中 → 取消顶部导航选中 + 加载歌单歌曲（仅左键）
    private async void PlaylistNavBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (Mouse.RightButton == MouseButtonState.Pressed) return;
        if (VM.SelectedPlaylist != null)
        {
            NavListBox.SelectedItem = null;
            await VM.LoadPlaylistSongs();
        }
    }

    // 右键菜单弹窗前拦截：点空白区域不弹菜单
    private void SongListBox_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        var hitElement = SongListBox.InputHitTest(Mouse.GetPosition(SongListBox));
        if (hitElement is not DependencyObject dep
            || FindVisualParent<ListBoxItem>(dep) == null)
        {
            e.Handled = true; // 阻止菜单弹出
        }
    }

    // 右键菜单：动态生成歌单子菜单
    private async void SongContextMenu_Opened(object sender, RoutedEventArgs e)
    {
        var toRemove = new List<object>();
        foreach (var item in SongContextMenu.Items)
        {
            if (item is MenuItem mi &&
                (mi.Header.ToString() == "从歌单移除" ||
                 mi.Header.ToString() == "下一首播放" ||
                 mi.Header.ToString() == "删除播放记录" ||
                 mi.Header.ToString() == "清空最近播放"))
                toRemove.Add(item);
            if (item is Separator && toRemove.Count > 0
                && SongContextMenu.Items.IndexOf(item) == SongContextMenu.Items.IndexOf(toRemove[^1]) + 1)
                toRemove.Add(item);
        }
        foreach (var r in toRemove) SongContextMenu.Items.Remove(r);

        if (SongListBox.SelectedItem is not Song selectedSong)
        {
            AddToPlaylistMenu.Items.Clear();
            AddToPlaylistMenu.Items.Add(new MenuItem { Header = "(请先选中歌曲)", IsEnabled = false });
            return;
        }

        if (VM.SelectedPlaylist != null && VM.IsSongInPlaylist(VM.SelectedPlaylist, selectedSong))
        {
            var removeItem = new MenuItem { Header = "从歌单移除" };
            removeItem.Click += async (_, _) => await VM.RemoveSongFromPlaylist(VM.SelectedPlaylist, selectedSong);
            SongContextMenu.Items.Insert(0, removeItem);
            SongContextMenu.Items.Insert(1, new Separator());
        }

        // 最近播放视图下的特殊菜单
        if (VM.CurrentView == "recent")
        {
            var delItem = new MenuItem { Header = "删除播放记录" };
            delItem.Click += (_, _) => VM.DeletePlayRecordCommand.Execute(selectedSong);
            SongContextMenu.Items.Insert(1, delItem);
        }

        // 下一首播放
        var nextItem = new MenuItem { Header = "下一首播放" };
        nextItem.Click += (_, _) => VM.AddToQueueNext(selectedSong);
        SongContextMenu.Items.Insert(SongContextMenu.Items.Count > 0 ? 1 : 0, nextItem);

        AddToPlaylistMenu.Items.Clear();
        foreach (var pl in VM.Playlists)
        {
            var item = new MenuItem { Header = $"添加到「{pl.Name}」", Tag = pl };
            item.Click += async (_, _) =>
            {
                if (SongListBox.SelectedItem is Song song && item.Tag is Playlist playlist)
                    await VM.AddSongToPlaylist(playlist, song);
            };
            AddToPlaylistMenu.Items.Add(item);
        }
        AddToPlaylistMenu.Items.Add(new Separator());
        var newItem = new MenuItem { Header = "+ 新建歌单" };
        newItem.Click += async (_, _) =>
        {
            var playlist = await VM.CreatePlaylist("新建歌单", "");
            if (playlist != null && SongListBox.SelectedItem is Song song2)
                await VM.AddSongToPlaylist(playlist, song2);
        };
        AddToPlaylistMenu.Items.Add(newItem);
    }

    // 歌单重命名
    private async void PlaylistRename_Click(object sender, RoutedEventArgs e)
    {
        if (VM.SelectedPlaylist is not Playlist pl) return;
        var dialog = new MusicPlayer.Views.InputDialog("重命名歌单", "请输入新名称:", pl.Name) { Owner = this };
        if (dialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(dialog.Result))
            await VM.RenamePlaylist(pl, dialog.Result);
    }

    // 歌词平滑滚动到指定行（居中 + 动画过渡）
    private System.Windows.Threading.DispatcherTimer? _scrollAnimTimer;
    private double _scrollTarget;

    private void SmoothScrollToLyric(int idx)
    {
        if (LyricsScrollViewer.ScrollableHeight <= 0) return;

        // 用 ItemContainerGenerator 获取目标行的实际位置
        var container = LyricsItemsControl.ItemContainerGenerator.ContainerFromIndex(idx) as FrameworkElement;
        if (container != null)
        {
            // 取容器中心点在 ScrollViewer 视口中的位置，换算为内容偏移
            var transform = container.TransformToAncestor(LyricsScrollViewer);
            var centerY = transform.Transform(new Point(0, container.ActualHeight / 2.0)).Y;
            _scrollTarget = LyricsScrollViewer.VerticalOffset + centerY
                - LyricsScrollViewer.ViewportHeight / 2.0;
        }
        else
        {
            // 兜底：容器不可见时用平均行高估算
            var lineH = LyricsScrollViewer.ScrollableHeight / Math.Max(1, LyricsItemsControl.Items.Count);
            _scrollTarget = idx * lineH - LyricsScrollViewer.ViewportHeight / 2.0 + lineH / 2.0;
        }
        _scrollTarget = Math.Max(0, Math.Min(_scrollTarget, LyricsScrollViewer.ScrollableHeight));

        // 平滑动画
        if (_scrollAnimTimer == null)
        {
            _scrollAnimTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(16)
            };
            _scrollAnimTimer.Tick += (_, _) =>
            {
                var cur = LyricsScrollViewer.VerticalOffset;
                var nxt = cur + (_scrollTarget - cur) * 0.3;
                if (Math.Abs(nxt - _scrollTarget) < 0.5)
                {
                    LyricsScrollViewer.ScrollToVerticalOffset(_scrollTarget);
                    _scrollAnimTimer?.Stop();
                }
                else
                {
                    LyricsScrollViewer.ScrollToVerticalOffset(nxt);
                }
            };
        }
        _scrollAnimTimer.Stop();
        _scrollAnimTimer.Start();
    }

    private static T? FindVisualParent<T>(DependencyObject child) where T : DependencyObject
    {
        var parent = VisualTreeHelper.GetParent(child);
        while (parent != null)
        {
            if (parent is T t) return t;
            parent = VisualTreeHelper.GetParent(parent);
        }
        return null;
    }

    // 歌词微调
    private void LyricBackward_Click(object sender, RoutedEventArgs e)
    {
        VM.LyricOffset -= 0.5;
        UpdateLyricTooltips();
    }
    private void LyricForward_Click(object sender, RoutedEventArgs e)
    {
        VM.LyricOffset += 0.5;
        UpdateLyricTooltips();
    }
    private void UpdateLyricTooltips()
    {
        var tip = $"已调整: {VM.LyricOffset:+#0.0;-#0.0} 秒";
        LyricBackBtn.ToolTip = tip;
        LyricFwdBtn.ToolTip = tip;
    }

    // 新建歌单 + 弹出重命名
    private async void PlaylistCreate_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new MusicPlayer.Views.InputDialog("新建歌单", "请输入歌单名称:", "新建歌单") { Owner = this };
        if (dialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(dialog.Result))
        {
            await VM.CreatePlaylist(dialog.Result, "");
        }
    }

    // 删除歌单
    private async void PlaylistDelete_Click(object sender, RoutedEventArgs e)
    {
        if (VM.SelectedPlaylist is not Playlist pl) return;
        var result = MessageBox.Show($"确定删除歌单「{pl.Name}」？", "确认", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (result == MessageBoxResult.Yes)
            await VM.DeletePlaylist(pl);
    }
}
