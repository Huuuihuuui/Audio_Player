// 歌词解析服务 —— 解析 LRC 歌词文件，并根据播放进度返回当前歌词行
using System.IO;
using System.Text.RegularExpressions;

namespace MusicPlayer.Services;

// 一行歌词数据
public class LyricsLine : System.ComponentModel.INotifyPropertyChanged
{
    // 该行歌词对应的时间点
    public TimeSpan Timestamp { get; set; }

    // 歌词文本
    public string Text { get; set; } = string.Empty;

    private bool _isCurrent;
    public bool IsCurrent
    {
        get => _isCurrent;
        set
        {
            if (_isCurrent != value)
            {
                _isCurrent = value;
                PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(IsCurrent)));
            }
        }
    }

    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
}

public class LyricsService
{
    private List<LyricsLine> _lyrics = new();

    // 已解析的歌词行列表（只读）
    public IReadOnlyList<LyricsLine> Lyrics => _lyrics.AsReadOnly();

    // 从 .lrc 文件加载歌词
    public bool LoadFromFile(string lrcFilePath)
    {
        if (!File.Exists(lrcFilePath)) return false;

        try
        {
            var text = File.ReadAllText(lrcFilePath);
            return Parse(text);
        }
        catch
        {
            return false;
        }
    }

    // 解析 LRC 文本内容 —— 使用正则提取时间标签 [mm:ss.xx]
    public bool Parse(string lrcContent)
    {
        _lyrics.Clear();

        // 正则匹配 LRC 时间标签：[02:31.45] 或 [02:31]
        var pattern = @"\[(\d{2}):(\d{2})(?:\.(\d{1,3}))?\]";

        foreach (var line in lrcContent.Split('\n'))
        {
            var trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed)) continue;

            var matches = Regex.Matches(trimmed, pattern);
            if (matches.Count == 0) continue;

            // 去除所有时间标签后得到歌词文本
            var text = Regex.Replace(trimmed, pattern, "").Trim();
            if (string.IsNullOrEmpty(text)) continue;

            // 支持同一行歌词对应多个时间戳（如 [01:00][02:00]歌词）
            foreach (Match match in matches)
            {
                var min = int.Parse(match.Groups[1].Value);
                var sec = int.Parse(match.Groups[2].Value);
                var ms = match.Groups[3].Success
                    ? int.Parse(match.Groups[3].Value.PadRight(3, '0'))
                    : 0;

                _lyrics.Add(new LyricsLine
                {
                    Timestamp = new TimeSpan(0, 0, min, sec, ms),
                    Text = text
                });
            }
        }

        // 按时间升序排列
        _lyrics = _lyrics.OrderBy(l => l.Timestamp).ToList();
        return _lyrics.Count > 0;
    }

    // 根据当前播放位置获取对应歌词行
    public string? GetCurrentLine(TimeSpan position)
    {
        if (_lyrics.Count == 0) return null;

        for (int i = _lyrics.Count - 1; i >= 0; i--)
        {
            if (_lyrics[i].Timestamp <= position)
                return _lyrics[i].Text;
        }

        return null;
    }

    // 根据当前播放位置获取歌词行索引（用于 UI 高亮滚动）
    public int GetCurrentLineIndex(TimeSpan position)
    {
        for (int i = _lyrics.Count - 1; i >= 0; i--)
        {
            if (_lyrics[i].Timestamp <= position)
                return i;
        }
        return -1;
    }

    // 根据音频文件路径自动查找同名 .lrc 或 .txt 歌词文件
    public void AutoLoadForAudio(string audioFilePath)
    {
        _lyrics.Clear();

        var lrcPath = Path.ChangeExtension(audioFilePath, ".lrc");
        if (!File.Exists(lrcPath))
        {
            lrcPath = Path.ChangeExtension(audioFilePath, ".txt");
            if (!File.Exists(lrcPath)) return;
        }

        LoadFromFile(lrcPath);
    }
}
