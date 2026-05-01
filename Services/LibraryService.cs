// 音乐库管理服务 —— 负责文件夹扫描、ID3 元数据解析、歌曲增删改查和播放记录
using System.IO;
using Microsoft.EntityFrameworkCore;
using MusicPlayer.Data;
using MusicPlayer.Models;

namespace MusicPlayer.Services;

public class LibraryService
{
    // 支持的音频格式列表
    private readonly string[] _supportedFormats = { ".mp3", ".flac", ".wav", ".wma", ".aac", ".ogg", ".m4a" };

    // 获取所有歌曲（按标题排序）
    public async Task<List<Song>> GetAllSongsAsync()
    {
        using var db = new MusicDbContext();
        return await db.Songs.OrderBy(s => s.Title).ToListAsync();
    }

    // 按关键词搜索歌曲：匹配歌名、艺术家或专辑名（使用 LinQ）
    public async Task<List<Song>> SearchSongsAsync(string keyword)
    {
        using var db = new MusicDbContext();
        var kw = keyword.ToLower();
        return await db.Songs
            .Where(s => s.Title.ToLower().Contains(kw)
                     || s.Artist.ToLower().Contains(kw)
                     || s.Album.ToLower().Contains(kw))
            .OrderBy(s => s.Title)
            .ToListAsync();
    }

    // 获取收藏歌曲列表
    public async Task<List<Song>> GetFavoriteSongsAsync()
    {
        using var db = new MusicDbContext();
        return await db.Songs.Where(s => s.IsFavorite).OrderBy(s => s.Title).ToListAsync();
    }

    // 获取最近播放的歌曲（按每首歌最新播放时间倒序，不丢重复播放）
    public async Task<List<Song>> GetRecentSongsAsync(int count = 50)
    {
        using var db = new MusicDbContext();
        var latestPlays = await db.PlayHistories
            .Include(h => h.Song)
            .GroupBy(h => h.SongId)
            .Select(g => new { Song = g.First().Song, LastPlayed = g.Max(h => h.PlayedAt) })
            .OrderByDescending(x => x.LastPlayed)
            .Take(count)
            .ToListAsync();
        return latestPlays.Select(x => x.Song).ToList();
    }

    // 扫描文件夹：递归查找所有支持的音频文件，解析元数据并入库
    public async Task<int> ScanFolderAsync(string folderPath, IProgress<string>? progress = null)
    {
        var scanned = 0;
        using var db = new MusicDbContext();

        foreach (var format in _supportedFormats)
        {
            foreach (var file in Directory.EnumerateFiles(folderPath, $"*{format}", SearchOption.AllDirectories))
            {
                try
                {
                    // 去重：已存在的文件路径跳过
                    var exists = await db.Songs.AnyAsync(s => s.FilePath == file);
                    if (exists) continue;

                    var song = ParseAudioFile(file);
                    db.Songs.Add(song);
                    scanned++;
                    progress?.Report($"已添加: {song.Title}");
                }
                catch
                {
                    // 异常处理：跳过损坏或无法读取的文件
                    progress?.Report($"无法读取: {file}");
                }
            }
        }

        await db.SaveChangesAsync();
        return scanned;
    }

    // 切换收藏状态
    public async Task SetFavoriteAsync(int songId, bool isFavorite)
    {
        using var db = new MusicDbContext();
        var song = await db.Songs.FindAsync(songId);
        if (song != null)
        {
            song.IsFavorite = isFavorite;
            await db.SaveChangesAsync();
        }
    }

    // 记录一次播放（写入 PlayHistory 表）
    public async Task RecordPlayAsync(int songId)
    {
        using var db = new MusicDbContext();
        db.PlayHistories.Add(new PlayHistory { SongId = songId });
        await db.SaveChangesAsync();
    }

    // 删除指定文件夹下的所有歌曲记录（不会删除磁盘文件）
    public async Task<int> DeleteSongsInFolderAsync(string folderPath)
    {
        using var db = new MusicDbContext();
        var normalizedPath = folderPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var songs = await db.Songs
            .Where(s => s.FilePath.StartsWith(normalizedPath + Path.DirectorySeparatorChar))
            .ToListAsync();
        db.Songs.RemoveRange(songs);
        await db.SaveChangesAsync();
        return songs.Count;
    }

    // 删除某首歌的播放记录
    public async Task DeletePlayHistoryAsync(int songId)
    {
        using var db = new MusicDbContext();
        var records = await db.PlayHistories.Where(h => h.SongId == songId).ToListAsync();
        db.PlayHistories.RemoveRange(records);
        await db.SaveChangesAsync();
    }

    // 清空全部播放记录
    public async Task ClearPlayHistoryAsync()
    {
        using var db = new MusicDbContext();
        db.PlayHistories.RemoveRange(db.PlayHistories);
        await db.SaveChangesAsync();
    }

    // 删除歌曲记录（不会删除磁盘上的文件）
    public async Task DeleteSongAsync(int songId)
    {
        using var db = new MusicDbContext();
        var song = await db.Songs.FindAsync(songId);
        if (song != null)
        {
            db.Songs.Remove(song);
            await db.SaveChangesAsync();
        }
    }

    // 解析音频文件元数据 —— 使用 TagLib# 读取 ID3 标签
    private static Song ParseAudioFile(string filePath)
    {
        var fileInfo = new FileInfo(filePath);

        try
        {
            var tagFile = TagLib.File.Create(filePath);
            var tag = tagFile.Tag;

            return new Song
            {
                FilePath = filePath,
                Title = string.IsNullOrWhiteSpace(tag.Title)
                    ? Path.GetFileNameWithoutExtension(filePath)   // 无标签时用文件名
                    : tag.Title,
                Artist = tag.FirstPerformer ?? "未知艺术家",
                Album = tag.Album ?? "未知专辑",
                DurationSeconds = tagFile.Properties.Duration.TotalSeconds,
                FileSize = fileInfo.Length,
                Format = fileInfo.Extension.ToLowerInvariant(),
                AddedDate = DateTime.Now
            };
        }
        catch
        {
            // 文件损坏或无标签时，使用文件基本信息兜底
            return new Song
            {
                FilePath = filePath,
                Title = Path.GetFileNameWithoutExtension(filePath),
                Artist = "未知艺术家",
                Album = "未知专辑",
                DurationSeconds = 0,
                FileSize = fileInfo.Length,
                Format = fileInfo.Extension.ToLowerInvariant(),
                AddedDate = DateTime.Now
            };
        }
    }
}
