// 播放历史记录 —— 对应数据库 PlayHistories 表，记录每次播放行为
namespace MusicPlayer.Models;

public class PlayHistory
{
    // 主键
    public int Id { get; set; }

    // 被播放的歌曲 ID（外键）
    public int SongId { get; set; }

    // 歌曲导航属性
    public Song Song { get; set; } = null!;

    // 播放时间
    public DateTime PlayedAt { get; set; } = DateTime.Now;
}
