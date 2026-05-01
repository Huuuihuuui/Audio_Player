// 应用配置模型 —— 不做数据库存储，仅用于 JSON 配置文件读写
namespace MusicPlayer.Models;

public class AppConfig
{
    // 播放音量（0~1）
    public double Volume { get; set; } = 0.8;

    // 主题名称：Dark / Light
    public string Theme { get; set; } = "Dark";

    // 已注册的音乐文件夹路径列表
    public List<string> WatchedFolders { get; set; } = new();

    // 上次关闭时正在播放的歌曲路径（用于恢复播放）
    public string? LastPlayedSongPath { get; set; }
}
