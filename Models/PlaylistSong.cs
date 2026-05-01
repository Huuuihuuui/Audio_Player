// 歌单-歌曲关联表（多对多中间表）—— 记录某首歌属于某个歌单
namespace MusicPlayer.Models;

public class PlaylistSong
{
    // 歌单 ID（联合主键之一）
    public int PlaylistId { get; set; }

    // 歌单导航属性
    public Playlist Playlist { get; set; } = null!;

    // 歌曲 ID（联合主键之一）
    public int SongId { get; set; }

    // 歌曲导航属性
    public Song Song { get; set; } = null!;

    // 加入歌单的时间
    public DateTime AddedDate { get; set; } = DateTime.Now;
}
