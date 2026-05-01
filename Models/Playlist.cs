// 歌单实体模型 —— 对应数据库 Playlists 表，存储一个歌单的基本信息
namespace MusicPlayer.Models;

public class Playlist
{
    // 主键
    public int Id { get; set; }

    // 歌单名称，如 "日语学习BGM"、"睡前歌单"
    public string Name { get; set; } = string.Empty;

    // 歌单描述（可选）
    public string Description { get; set; } = string.Empty;

    // 创建时间
    public DateTime CreatedDate { get; set; } = DateTime.Now;

    // 多对多中间表：一个歌单包含多首歌曲（导航属性）
    public ICollection<PlaylistSong> PlaylistSongs { get; set; } = new List<PlaylistSong>();
}
