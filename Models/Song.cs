// 歌曲实体模型 —— 对应数据库 Songs 表，存储每首歌曲的元数据
using System.ComponentModel.DataAnnotations;

namespace MusicPlayer.Models;

public class Song
{
    // 主键，自动递增
    public int Id { get; set; }

    // 音频文件在磁盘上的绝对路径（唯一索引）
    [Required]
    public string FilePath { get; set; } = string.Empty;

    // 歌曲标题（来自 ID3 标签，若无则取文件名）
    public string Title { get; set; } = string.Empty;

    // 艺术家 / 表演者
    public string Artist { get; set; } = string.Empty;

    // 所属专辑
    public string Album { get; set; } = string.Empty;

    // 播放时长（秒），用于列表显示
    public double DurationSeconds { get; set; }

    // 文件大小（字节）
    public long FileSize { get; set; }

    // 文件扩展名，如 .mp3 .flac .wav
    public string Format { get; set; } = string.Empty;

    // 入库时间
    public DateTime AddedDate { get; set; } = DateTime.Now;

    // 是否已收藏
    public bool IsFavorite { get; set; }

    // 多对多：一首歌可以属于多个歌单（导航属性）
    public ICollection<PlaylistSong> PlaylistSongs { get; set; } = new List<PlaylistSong>();

    // 一对多：一首歌可以有多次播放记录（导航属性）
    public ICollection<PlayHistory> PlayHistories { get; set; } = new List<PlayHistory>();
}
