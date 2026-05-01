// 主窗口 ViewModel —— 协调所有 Service，为 MainWindow 提供数据绑定和命令
using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Input;
using MusicPlayer.Helpers;
using MusicPlayer.Models;
using MusicPlayer.Services;

namespace MusicPlayer.ViewModels;

public enum PlayMode { ListLoop, SingleRepeat, Shuffle }

public class MainViewModel : BaseViewModel
{
    // -- 依赖的服务 --
    private readonly PlaybackService _playback;
    private readonly LibraryService _library;
    private readonly PlaylistService _playlistService;
    private readonly LyricsService _lyrics;

    // 频谱刷新定时器（每 50ms 取一次 FFT 数据）
    private System.Windows.Threading.DispatcherTimer? _spectrumTimer;

    public MainViewModel()
    {
        // 初始化频谱柱
        SpectrumBars = new ObservableCollection<SpectrumBar>();
        for (int i = 0; i < 64; i++)
            SpectrumBars.Add(new SpectrumBar());

        // 初始化服务
        _playback = new PlaybackService();
        _library = new LibraryService();
        _playlistService = new PlaylistService();
        _lyrics = new LyricsService();

        // 初始化命令
        PlayPauseCommand = new RelayCommand(PlayPause);
        StopCommand = new RelayCommand(Stop);
        NextCommand = new AsyncRelayCommand(Next);
        PreviousCommand = new AsyncRelayCommand(Previous);
        ScanFolderCommand = new AsyncRelayCommand(ScanFolder);
        AddWatchedFolderCommand = new RelayCommand(AddWatchedFolder);
        RemoveWatchedFolderCommand = new RelayCommand<string>(RemoveWatchedFolder);
        AddToPlaylistCommand = new RelayCommand<Song>(async (s) => { if (s != null) await AddToPlaylist(s); });
        ToggleFavoriteCommand = new RelayCommand<Song>(async (s) => { if (s != null) await ToggleFavorite(s); });
        CyclePlayModeCommand = new RelayCommand(CyclePlayMode);
        ToggleQueuePanelCommand = new RelayCommand(() => QueuePanelVisible = !QueuePanelVisible);
        RemoveFromQueueCommand = new RelayCommand<Song>(s => { if (s != null) RemoveFromQueue(s); });
        DeletePlayRecordCommand = new RelayCommand<Song>(async s => { if (s != null) await DeletePlayRecord(s); });
        ClearPlayHistoryCommand = new RelayCommand(async () => await ClearPlayHistory());

        // 从配置文件恢复已添加的文件夹
        var config = ConfigManager.Load();
        foreach (var path in config.WatchedFolders)
        {
            if (Directory.Exists(path))
                WatchedFolders.Add(path);
        }

        // 启动频谱数据采集定时器
        _spectrumTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(8) //每 8ms 刷新一次频谱数据 125fps
        };
        _spectrumTimer.Tick += (_, _) =>
        {
            var samples = _playback.GetFFTSamples(64);
            UpdateSpectrumBars(samples);
            SpectrumUpdated?.Invoke(samples);
        };
        _spectrumTimer.Start();

        // 订阅播放状态变化 → 更新 UI 按钮文本
        _playback.StateChanged += state =>
        {
            OnPropertyChanged(nameof(IsPlaying));
            OnPropertyChanged(nameof(PlayPauseText));
        };

        // 订阅播放位置变化 → 更新进度条和歌词
        _playback.PositionChanged += pos =>
        {
            OnPropertyChanged(nameof(CurrentPosition));
            OnPropertyChanged(nameof(CurrentPositionText));
            OnPropertyChanged(nameof(ProgressPercent));
            UpdateLyrics();


            OnPropertyChanged(nameof(TotalDurationSeconds));
            OnPropertyChanged(nameof(TotalDurationText));

        
        };

        // 播放结束自动切下一首
        _playback.PlaybackFinished += async () =>
        {
            await Next();
        };

        // 异步加载初始数据
        RefreshData().ConfigureAwait(false);
    }

    // ========== 数据绑定属性 ==========

    // 歌曲列表（主视图列表数据源）
    public ObservableCollection<Song> Songs { get; } = new();

    // 歌单列表（左侧导航数据源）
    public ObservableCollection<Playlist> Playlists { get; } = new();

    // 当前选中歌单中的歌曲
    public ObservableCollection<Song> CurrentPlaylistSongs { get; } = new();

    // 播放队列（当前播放列表）
    public ObservableCollection<Song> PlayQueue { get; } = new();
    private int _queueIndex = -1;

    // 当前队列播放位置（供 UI 高亮）
    public int CurrentQueueIndex
    {
        get => _queueIndex;
        private set { _queueIndex = value; OnPropertyChanged(); QueueIndexChanged?.Invoke(value); }
    }
    // 队列位置变化事件（MainWindow 订阅来更新高亮）
    public event Action<int>? QueueIndexChanged;

    // 当前播放歌曲（供全视图高亮）
    private Song? _currentPlayingSong;
    public Song? CurrentPlayingSong
    {
        get => _currentPlayingSong;
        private set { SetProperty(ref _currentPlayingSong, value); CurrentPlayingChanged?.Invoke(); }
    }
    public event Action? CurrentPlayingChanged;

    private bool _queuePanelVisible;
    public bool QueuePanelVisible
    {
        get => _queuePanelVisible;
        set => SetProperty(ref _queuePanelVisible, value);
    }

    // 切换队列面板显示/隐藏
    public ICommand ToggleQueuePanelCommand { get; }
    // 从队列中移除歌曲
    public ICommand RemoveFromQueueCommand { get; }
    public ICommand DeletePlayRecordCommand { get; }
    public ICommand ClearPlayHistoryCommand { get; }

    private Song? _selectedSong;
    // 当前选中的歌曲（双击或点击列表项）
    public Song? SelectedSong
    {
        get => _selectedSong;
        set => SetProperty(ref _selectedSong, value);
    }

    private Playlist? _selectedPlaylist;
    // 当前选中的歌单（左侧歌单导航）
    public Playlist? SelectedPlaylist
    {
        get => _selectedPlaylist;
        set => SetProperty(ref _selectedPlaylist, value);
    }

    private string _searchText = string.Empty;
    // 搜索关键字（实时搜索，输入即触发）
    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value))
                _ = Search();
        }
    }

    // 当前显示视图：all / favorites / recent
    private string _currentView = "all";
    public string CurrentView
    {
        get => _currentView;
        set
        {
            if (SetProperty(ref _currentView, value))
                _ = SwitchView();
        }
    }

    // 频谱柱状图数据（64 根柱子，每根都支持属性变更通知）
    public ObservableCollection<SpectrumBar> SpectrumBars { get; }

    // 频谱数据更新事件（MainWindow 订阅后传给 SpectrumView 绘制）
    public event Action<float[]>? SpectrumUpdated;

    // 频谱刷新定时器回调中更新每根柱子的高度
    private void UpdateSpectrumBars(float[] samples)
    {
        for (int i = 0; i < 64 && i < samples.Length; i++)
        {
            SpectrumBars[i].Height = Math.Clamp(samples[i] * 80, 0.5, 80);
        }
    }

    // 歌词行集合（右侧面板显示）
    public ObservableCollection<LyricsLine> LyricsLines { get; } = new();

    private string _currentLyricLine = string.Empty;
    public string CurrentLyricLine
    {
        get => _currentLyricLine;
        set => SetProperty(ref _currentLyricLine, value);
    }

    // 歌词时间偏移（秒），正值=歌词提前，负值=歌词延后
    public double LyricOffset { get; set; }

    // 当前歌词行索引（供 MainWindow 高亮滚动）
    public event Action<int>? CurrentLyricChanged;

    // 是否正在播放（用于切换播放/暂停按钮）
    public bool IsPlaying => _playback.State == PlaybackState.Playing;

    // 播放/暂停按钮文本：播放中显示"⏸"，否则显示"▶"
    public string PlayPauseText => _playback.State == PlaybackState.Playing ? "⏸" : "▶";

    // 播放模式
    private PlayMode _playMode = PlayMode.ListLoop;
    private List<int>? _shuffleOrder; // 洗牌后的索引序列
    private int _shufflePos;

    public PlayMode CurrentPlayMode
    {
        get => _playMode;
        set
        {
            SetProperty(ref _playMode, value);
            OnPropertyChanged(nameof(PlayModeText));
            if (value == PlayMode.Shuffle)
                Reshuffle(Songs.ToList());
        }
    }
    public string PlayModeText => CurrentPlayMode switch
    {
        PlayMode.ListLoop => "🔁",
        PlayMode.SingleRepeat => "🔂",
        PlayMode.Shuffle => "🔀",
        _ => "🔁"
    };

    private void CyclePlayMode()
    {
        CurrentPlayMode = (PlayMode)(((int)CurrentPlayMode + 1) % 3);

        if (CurrentPlayMode == PlayMode.Shuffle && PlayQueue.Count > 1)
        {
            // Fisher-Yates 原地洗牌（保持当前播放位置）
            var current = PlayQueue[CurrentQueueIndex >= 0 ? CurrentQueueIndex : 0];
            var list = PlayQueue.ToList();
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = Random.Shared.Next(i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
            PlayQueue.Clear();
            foreach (var s in list) PlayQueue.Add(s);
            CurrentQueueIndex = PlayQueue.IndexOf(current);
        }

        if (CurrentPlayMode != PlayMode.Shuffle)
        {
            _shuffleOrder = null;
        }
    }

    // Fisher-Yates 洗牌
    private void Reshuffle(List<Song> songs)
    {
        var count = songs.Count;
        _shuffleOrder = Enumerable.Range(0, count).ToList();
        for (int i = count - 1; i > 0; i--)
        {
            int j = Random.Shared.Next(i + 1);
            (_shuffleOrder[i], _shuffleOrder[j]) = (_shuffleOrder[j], _shuffleOrder[i]);
        }
        _shufflePos = 0;
    }

    // 当前播放位置（秒）
    public double CurrentPosition => _playback.CurrentPositionSeconds;

    // 总时长（秒，供进度条点击跳转）
    public double TotalDurationSeconds => _playback.TotalDurationSeconds;
    public void SeekTo(double seconds) => _playback.Seek(seconds);

    // 当前播放位置的格式化文本（如 "1:23"）
    public string CurrentPositionText => FormatTime(_playback.CurrentPositionSeconds);

    // 总播放时长的格式化文本
    public string TotalDurationText => FormatTime(_playback.TotalDurationSeconds);

    // 进度百分比（0~100），Slider 双向绑定
    public double ProgressPercent
    {
        get => _playback.TotalDurationSeconds > 0
            ? _playback.CurrentPositionSeconds / _playback.TotalDurationSeconds * 100
            : 0;
        set
        {
            if (_playback.TotalDurationSeconds > 0)
                _playback.Seek(value / 100.0 * _playback.TotalDurationSeconds);
        }
    }

    private double _volume = 0.8;
    // 音量（0~1），绑定到音量滑块
    public double Volume
    {
        get => _volume;
        set
        {
            if (SetProperty(ref _volume, value))
                _playback.Volume = (float)value;
        }
    }

    private string _currentSongTitle = "未在播放";
    // 当前播放歌曲的标题-歌手名
    public string CurrentSongTitle
    {
        get => _currentSongTitle;
        set => SetProperty(ref _currentSongTitle, value);
    }


    // ========== 右侧详情面板属性 ==========
    private string _detailTitle = "未在播放";
    public string DetailTitle { get => _detailTitle; set => SetProperty(ref _detailTitle, value); }

    private string _detailArtist = "";
    public string DetailArtist { get => _detailArtist; set => SetProperty(ref _detailArtist, value); }

    private string _detailAlbum = "";
    public string DetailAlbum { get => _detailAlbum; set => SetProperty(ref _detailAlbum, value); }

    private string _detailFormat = "";
    public string DetailFormat { get => _detailFormat; set => SetProperty(ref _detailFormat, value); }

    // 音频规格说明文字（如 "44.1kHz / 16bit / 立体声"）
    public string DetailSpecs => _playback.FormatInfo;

    private byte[]? _coverImageData;
    public byte[]? CoverImageData
    {
        get => _coverImageData;
        set { SetProperty(ref _coverImageData, value); OnPropertyChanged(nameof(HasCover)); }
    }
    public bool HasCover => _coverImageData != null && _coverImageData.Length > 0;

    // 用户添加的监听文件夹路径列表（可多选）
    public ObservableCollection<string> WatchedFolders { get; } = new();

    private string? _selectedWatchedFolder;
    // 当前选中的监听文件夹（用于删除按钮）
    public string? SelectedWatchedFolder
    {
        get => _selectedWatchedFolder;
        set => SetProperty(ref _selectedWatchedFolder, value);
    }

    private string _statusText = "就绪";
    // 状态栏文字（扫描进度等）
    public string StatusText
    {
        get => _statusText;
        set => SetProperty(ref _statusText, value);
    }

    private bool _isLoading;
    // 是否正在加载中
    public bool IsLoading
    {
        get => _isLoading;
        set => SetProperty(ref _isLoading, value);
    }

    // ========== 命令（绑定到 View 按钮） ==========
    public ICommand PlayPauseCommand { get; }
    public ICommand StopCommand { get; }
    public ICommand NextCommand { get; }
    public ICommand PreviousCommand { get; }
    public ICommand ScanFolderCommand { get; }
    public ICommand AddWatchedFolderCommand { get; }
    public ICommand RemoveWatchedFolderCommand { get; }
    public ICommand AddToPlaylistCommand { get; }
    public ICommand ToggleFavoriteCommand { get; }
    public ICommand CyclePlayModeCommand { get; }

    // ========== 播放控制方法 ==========

    // 双击歌曲播放
    public void PlaySong(Song song)
    {
        // 如果歌曲已在队列中，直接跳到该位置
        var qIdx = PlayQueue.IndexOf(song);
        if (qIdx >= 0)
        {
            CurrentQueueIndex = qIdx;
            SelectedSong = song;
            _playback.Load(song.FilePath);
            _playback.Play();
            UpdateSongDetail(song);
            return;
        }

        // 不在队列中：用当前显示的列表建队列
        var list = Songs.ToList();
        var idx = list.IndexOf(song);
        SetQueueAndPlay(list, Math.Max(0, idx));

    }

    private async void UpdateSongDetail(Song song)
    {
        CurrentPlayingSong = song;
        _lyrics.AutoLoadForAudio(song.FilePath);
        LyricsLines.Clear();
        foreach (var l in _lyrics.Lyrics) LyricsLines.Add(l);
        CurrentSongTitle = $"{song.Title} - {song.Artist}";
        CurrentLyricLine = string.Empty;
        _lastLyricIdx = -1;
        DetailTitle = song.Title;
        DetailArtist = song.Artist;
        DetailAlbum = song.Album;
        DetailFormat = song.Format.ToUpperInvariant();
        CoverImageData = ExtractCoverImage(song.FilePath);
        OnPropertyChanged(nameof(DetailSpecs));
        await _library.RecordPlayAsync(song.Id);

        // 在最近播放视图中立即刷新
        if (CurrentView == "recent")
            await SwitchView();
    }

    // 播放/暂停切换：正在播放 → 暂停；暂停/停止 → 播放
    private void PlayPause()
    {
        if (_playback.State == PlaybackState.Playing)
        {
            _playback.Pause();
        }
        else if (_playback.State == PlaybackState.Paused)
        {
            _playback.Play();
        }
        else if (SelectedSong != null)
        {
            LoadAndPlay();
        }
    }

    // 加载并播放当前选中歌曲
    private void LoadAndPlay()
    {
        try
        {
            if (SelectedSong == null || !File.Exists(SelectedSong.FilePath))
            {
                StatusText = SelectedSong == null ? "请先选择歌曲" : $"文件不存在: {SelectedSong.FilePath}";
                return;
            }

            _playback.Load(SelectedSong.FilePath);
            _playback.Play();
            _lyrics.AutoLoadForAudio(SelectedSong.FilePath);
            LyricsLines.Clear();
            foreach (var l in _lyrics.Lyrics) LyricsLines.Add(l);
            CurrentSongTitle = $"{SelectedSong.Title} - {SelectedSong.Artist}";


            // 填充右侧详情面板
            DetailTitle = SelectedSong.Title;
            DetailArtist = SelectedSong.Artist;
            DetailAlbum = SelectedSong.Album;
            DetailFormat = SelectedSong.Format.ToUpperInvariant();
            CoverImageData = ExtractCoverImage(SelectedSong.FilePath);
            OnPropertyChanged(nameof(DetailSpecs));

            _ = _library.RecordPlayAsync(SelectedSong.Id);
        }
        catch (Exception ex)
        {
            StatusText = $"播放失败: {ex.Message}";
        }
    }

    // 从音频文件中提取内嵌封面图片
    private static byte[]? ExtractCoverImage(string filePath)
    {
        try
        {
            var tagFile = TagLib.File.Create(filePath);
            var pictures = tagFile.Tag.Pictures;
            if (pictures != null && pictures.Length > 0)
                return pictures[0].Data.Data;
        }
        catch { }
        return null;
    }

    // 停止播放，清空当前歌曲和歌词信息
    private void Stop()
    {
        _playback.Stop();
        CurrentSongTitle = "未在播放";
        CurrentLyricLine = string.Empty;
    }

    // 下一首：从播放队列取
    private async Task Next()
    {
        if (PlayQueue.Count == 0)
        {
            // 队列为空时，用当前显示的歌曲列表填充
            var songs = Songs.ToList();
            if (songs.Count == 0) return;

            if (CurrentPlayMode == PlayMode.Shuffle)
            {
                if (_shuffleOrder == null || _shufflePos >= _shuffleOrder.Count)
                    Reshuffle(songs);
                foreach (var i in _shuffleOrder!) PlayQueue.Add(songs[i]);
            }
            else
            {
                foreach (var s in songs) PlayQueue.Add(s);
            }
            CurrentQueueIndex = PlayQueue.ToList().FindIndex(q => q.FilePath == _playback.CurrentFilePath);
            if (CurrentQueueIndex < 0) CurrentQueueIndex = 0;
        }

        if (CurrentPlayMode == PlayMode.SingleRepeat)
        {
            // 单曲循环：重复当前，不移动队列指针
        }
        else
        {
            CurrentQueueIndex++;
            if (_queueIndex >= PlayQueue.Count)
            {
                // 队列播完：回绕到开头，不重建
                CurrentQueueIndex = 0;
            }
        }

        if (CurrentQueueIndex >= 0 && CurrentQueueIndex < PlayQueue.Count)
        {
            var song = PlayQueue[CurrentQueueIndex];
            SelectedSong = song;
            _playback.Load(song.FilePath);
            _playback.Play();
            UpdateSongDetail(song);
        }
    }

    // 上一首
    private async Task Previous()
    {
        var songs = Songs.ToList();
        if (songs.Count == 0) return;

        var current = songs.FindIndex(s => s.FilePath == _playback.CurrentFilePath);
        var prevIndex = current > 0 ? current - 1 : songs.Count - 1;
        SelectedSong = songs[prevIndex];
        _playback.Load(SelectedSong.FilePath);
        _playback.Play();
        _lyrics.AutoLoadForAudio(SelectedSong.FilePath);
        CurrentSongTitle = $"{SelectedSong.Title} - {SelectedSong.Artist}";
        await _library.RecordPlayAsync(SelectedSong.Id);
    }

    // ========== 音乐库操作 ==========

    // 弹出文件夹选择对话框，把选中路径加入监听列表
    private void AddWatchedFolder()
    {
        var path = FolderBrowser.ShowDialog("请选择音乐文件夹");
        if (!string.IsNullOrEmpty(path) && !WatchedFolders.Contains(path))
        {
            WatchedFolders.Add(path);
            SaveWatchedFolders();
        }
    }

    // 移除监听文件夹，同时删除数据库中该路径下的歌曲记录
    private async void RemoveWatchedFolder(string? path)
    {
        if (string.IsNullOrEmpty(path)) return;
        WatchedFolders.Remove(path);
        SaveWatchedFolders();

        IsLoading = true;
        StatusText = $"正在清理 {path} ...";
        try
        {
            var removed = await _library.DeleteSongsInFolderAsync(path);
            StatusText = $"已移除路径，清理 {removed} 首歌曲";
        }
        catch (Exception ex)
        {
            StatusText = $"清理出错: {ex.Message}";
        }
        IsLoading = false;
        await RefreshSongs();
    }

    // 持久化监听文件夹列表到配置文件
    private void SaveWatchedFolders()
    {
        var config = ConfigManager.Load();
        config.WatchedFolders = WatchedFolders.ToList();
        ConfigManager.Save(config);
    }

    // 扫描所有已添加的文件夹
    private async Task ScanFolder()
    {
        if (WatchedFolders.Count == 0)
        {
            StatusText = "请先添加音乐文件夹";
            return;
        }

        IsLoading = true;
        var totalCount = 0;
        foreach (var path in WatchedFolders.ToList())
        {
            if (!Directory.Exists(path))
            {
                WatchedFolders.Remove(path);
                continue;
            }
            StatusText = $"正在扫描: {path} ...";
            try
            {
                var count = await _library.ScanFolderAsync(path, new Progress<string>(msg => StatusText = msg));
                totalCount += count;
            }
            catch (Exception ex)
            {
                StatusText = $"扫描出错: {ex.Message}";
            }
        }
        StatusText = $"扫描完成，共新增 {totalCount} 首歌曲";
        IsLoading = false;
        SaveWatchedFolders();
        await RefreshSongs();
    }

    // 切换视图：根据 CurrentView 切换歌曲列表
    private async Task SwitchView()
    {
        Songs.Clear();
        List<Song> results;

        switch (CurrentView)
        {
            case "favorites":
                results = await _library.GetFavoriteSongsAsync();
                break;
            case "recent":
                results = await _library.GetRecentSongsAsync();
                break;
            default:
                results = await _library.GetAllSongsAsync();
                break;
        }

        foreach (var song in results)
            Songs.Add(song);
    }

    // 搜索歌曲：空关键字时返回全部
    private async Task Search()
    {
        Songs.Clear();
        List<Song> results;
        if (string.IsNullOrWhiteSpace(SearchText))
            results = await _library.GetAllSongsAsync();
        else
            results = await _library.SearchSongsAsync(SearchText);

        foreach (var song in results)
            Songs.Add(song);
    }

    // ========== 歌单操作 ==========

    // 创建歌单（公开，供 MainWindow 调用）
    public async Task<Playlist?> CreatePlaylist(string name, string desc)
    {
        var pl = await _playlistService.CreatePlaylistAsync(name, desc);
        await RefreshPlaylists();
        return pl;
    }

    // 将歌曲添加到指定歌单（公开）
    public async Task AddSongToPlaylist(Playlist playlist, Song song)
    {
        await _playlistService.AddSongToPlaylistAsync(playlist.Id, song.Id);
    }

    // 添加到播放队列的下一首（已在队列中则移到最后）
    public void AddToQueueNext(Song song)
    {
        var existingIdx = PlayQueue.IndexOf(song);
        if (existingIdx >= 0)
        {
            // 先移除再插入到当前位置之后（避免 Move 的索引混乱）
            PlayQueue.RemoveAt(existingIdx);
            if (existingIdx < CurrentQueueIndex)
                CurrentQueueIndex--;
        }

        var target = Math.Min(CurrentQueueIndex + 1, PlayQueue.Count);
        PlayQueue.Insert(target, song);
    }

    // 删除单条播放记录
    private async Task DeletePlayRecord(Song song)
    {
        await _library.DeletePlayHistoryAsync(song.Id);
        if (CurrentView == "recent") await SwitchView();
    }

    // 清空全部播放记录
    private async Task ClearPlayHistory()
    {
        await _library.ClearPlayHistoryAsync();
        if (CurrentView == "recent")
        {
            Songs.Clear();
        }
    }

    // 从队列移除
    public void RemoveFromQueue(Song song)
    {
        var idx = PlayQueue.IndexOf(song);
        if (idx >= 0)
        {
            PlayQueue.RemoveAt(idx);
            if (idx < CurrentQueueIndex) CurrentQueueIndex--;
        }
    }

    // 用指定歌曲列表填充队列并从第一首开始播放
    public void SetQueueAndPlay(List<Song> songs, int startIndex = 0)
    {
        PlayQueue.Clear();
        foreach (var s in songs) PlayQueue.Add(s);
        CurrentQueueIndex = Math.Max(0, Math.Min(startIndex, songs.Count - 1));
        if (PlayQueue.Count > 0)
        {
            var song = PlayQueue[CurrentQueueIndex];
            SelectedSong = song;
            _playback.Load(song.FilePath);
            _playback.Play();
            UpdateSongDetail(song);
        }
    }

    // 检查歌曲是否在歌单中
    public bool IsSongInPlaylist(Playlist playlist, Song song)
    {
        return Songs.Contains(song);
    }

    // 重命名歌单
    public async Task RenamePlaylist(Playlist playlist, string newName)
    {
        using var db = new MusicPlayer.Data.MusicDbContext();
        var entity = await db.Playlists.FindAsync(playlist.Id);
        if (entity != null)
        {
            entity.Name = newName;
            await db.SaveChangesAsync();
        }
        await RefreshPlaylists();
    }

    // 删除歌单
    public async Task DeletePlaylist(Playlist playlist)
    {
        if (SelectedPlaylist?.Id == playlist.Id)
            SelectedPlaylist = null;
        await _playlistService.DeletePlaylistAsync(playlist.Id);
        await RefreshPlaylists();
        await RefreshSongs();
    }

    // 从歌单移除歌曲（公开）
    public async Task RemoveSongFromPlaylist(Playlist playlist, Song song)
    {
        await _playlistService.RemoveSongFromPlaylistAsync(playlist.Id, song.Id);
        await LoadPlaylistSongs();
    }

    // 切换歌曲收藏状态
    private async Task ToggleFavorite(Song song)
    {
        await _library.SetFavoriteAsync(song.Id, !song.IsFavorite);
        song.IsFavorite = !song.IsFavorite;
        await SwitchView(); // 如果在收藏视图则刷新
    }

    // 将歌曲添加到当前选中的歌单
    private async Task AddToPlaylist(Song song)
    {
        if (SelectedPlaylist != null)
        {
            await _playlistService.AddSongToPlaylistAsync(SelectedPlaylist.Id, song.Id);
            await LoadPlaylistSongs();
        }
    }

    // 加载选中歌单的歌曲列表
    public async Task LoadPlaylistSongs()
    {
        if (SelectedPlaylist == null)
        {
            // 取消选中歌单，回到全歌曲视图
            CurrentView = "all";
            return;
        }

        // 把歌单歌曲加载到主列表
        Songs.Clear();
        var playlist = await _playlistService.GetPlaylistWithSongsAsync(SelectedPlaylist.Id);
        if (playlist != null)
        {
            foreach (var ps in playlist.PlaylistSongs)
                Songs.Add(ps.Song);
        }
    }

    // 强制切换回全部歌曲视图
    public async Task SwitchToAllSongs()
    {
        Songs.Clear();
        var songs = await _library.GetAllSongsAsync();
        foreach (var s in songs) Songs.Add(s);
    }

    // 刷新全部数据（歌曲 + 歌单）
    public async Task RefreshData()
    {
        await RefreshSongs();
        await RefreshPlaylists();
    }

    private async Task RefreshSongs()
    {
        var songs = string.IsNullOrWhiteSpace(SearchText)
            ? await _library.GetAllSongsAsync()
            : await _library.SearchSongsAsync(SearchText);

        Songs.Clear();
        foreach (var song in songs)
            Songs.Add(song);
    }

    private async Task RefreshPlaylists()
    {
        var playlists = await _playlistService.GetAllPlaylistsAsync();
        Playlists.Clear();
        foreach (var pl in playlists)
            Playlists.Add(pl);
    }

    // ========== 歌词同步 ==========

    // 根据当前播放位置更新歌词显示
    private int _lastLyricIdx = -1;
    private void UpdateLyrics()
    {
        var pos = TimeSpan.FromSeconds(_playback.CurrentPositionSeconds + LyricOffset);
        var line = _lyrics.GetCurrentLine(pos);
        if (line != null)
            CurrentLyricLine = line;

        var idx = _lyrics.GetCurrentLineIndex(pos);
        if (idx != _lastLyricIdx)
        {
            // 取消上一行高亮
            if (_lastLyricIdx >= 0 && _lastLyricIdx < LyricsLines.Count)
                LyricsLines[_lastLyricIdx].IsCurrent = false;

            _lastLyricIdx = idx;

            // 高亮当前行
            if (idx >= 0 && idx < LyricsLines.Count)
            {
                LyricsLines[idx].IsCurrent = true;
                CurrentLyricChanged?.Invoke(idx);
            }
        }
    }

    // 秒数格式化为 m:ss 或 h:mm:ss
    private static string FormatTime(double seconds)
    {
        var ts = TimeSpan.FromSeconds(seconds);
        return ts.Hours > 0 ? ts.ToString(@"h\:mm\:ss") : ts.ToString(@"m\:ss");
    }
}
