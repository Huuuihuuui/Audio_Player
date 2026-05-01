// 歌单管理服务 —— 歌单 CRUD、歌曲添加/移除、JSON 格式的导入导出
using System.IO;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using MusicPlayer.Data;
using MusicPlayer.Models;

namespace MusicPlayer.Services;

public class PlaylistService
{
    // 获取所有歌单列表
    public async Task<List<Playlist>> GetAllPlaylistsAsync()
    {
        using var db = new MusicDbContext();
        return await db.Playlists.Include(p => p.PlaylistSongs).ToListAsync();
    }

    // 获取单个歌单及其包含的所有歌曲
    public async Task<Playlist?> GetPlaylistWithSongsAsync(int playlistId)
    {
        using var db = new MusicDbContext();
        return await db.Playlists
            .Include(p => p.PlaylistSongs)
                .ThenInclude(ps => ps.Song)
            .FirstOrDefaultAsync(p => p.Id == playlistId);
    }

    // 创建新歌单
    public async Task<Playlist> CreatePlaylistAsync(string name, string description = "")
    {
        using var db = new MusicDbContext();
        var playlist = new Playlist { Name = name, Description = description };
        db.Playlists.Add(playlist);
        await db.SaveChangesAsync();
        return playlist;
    }

    // 删除歌单（级联删除关联记录）
    public async Task DeletePlaylistAsync(int playlistId)
    {
        using var db = new MusicDbContext();
        var playlist = await db.Playlists.FindAsync(playlistId);
        if (playlist != null)
        {
            db.Playlists.Remove(playlist);
            await db.SaveChangesAsync();
        }
    }

    // 向歌单添加歌曲（自动去重）
    public async Task AddSongToPlaylistAsync(int playlistId, int songId)
    {
        using var db = new MusicDbContext();
        var exists = await db.PlaylistSongs.AnyAsync(ps => ps.PlaylistId == playlistId && ps.SongId == songId);
        if (!exists)
        {
            db.PlaylistSongs.Add(new PlaylistSong { PlaylistId = playlistId, SongId = songId });
            await db.SaveChangesAsync();
        }
    }

    // 从歌单中移除歌曲
    public async Task RemoveSongFromPlaylistAsync(int playlistId, int songId)
    {
        using var db = new MusicDbContext();
        var entry = await db.PlaylistSongs
            .FirstOrDefaultAsync(ps => ps.PlaylistId == playlistId && ps.SongId == songId);
        if (entry != null)
        {
            db.PlaylistSongs.Remove(entry);
            await db.SaveChangesAsync();
        }
    }

    // 将歌单导出为 JSON 字符串
    public async Task<string> ExportPlaylistAsync(int playlistId)
    {
        var playlist = await GetPlaylistWithSongsAsync(playlistId);
        if (playlist == null) return string.Empty;

        var data = new
        {
            playlist.Name,
            playlist.Description,
            Songs = playlist.PlaylistSongs.Select(ps => new
            {
                ps.Song.Title,
                ps.Song.Artist,
                ps.Song.Album,
                ps.Song.FilePath,
                ps.Song.DurationSeconds,
                ps.Song.Format
            })
        };

        return JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
    }

    // 将歌单导出为 JSON 文件
    public async Task SavePlaylistToFileAsync(int playlistId, string filePath)
    {
        var json = await ExportPlaylistAsync(playlistId);
        await File.WriteAllTextAsync(filePath, json);
    }

    // 从 JSON 文件导入歌单 —— 会尝试匹配已入库的歌曲
    public async Task<Playlist> ImportPlaylistFromFileAsync(string filePath)
    {
        var json = await File.ReadAllTextAsync(filePath);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var name = root.GetProperty("Name").GetString() ?? "导入歌单";
        var description = root.GetProperty("Description").GetString() ?? "";

        var playlist = await CreatePlaylistAsync(name, description);

        if (root.TryGetProperty("Songs", out var songs))
        {
            foreach (var songElement in songs.EnumerateArray())
            {
                var songPath = songElement.GetProperty("FilePath").GetString();
                if (string.IsNullOrEmpty(songPath)) continue;

                using var db = new MusicDbContext();
                var song = await db.Songs.FirstOrDefaultAsync(s => s.FilePath == songPath);
                if (song != null)
                {
                    await AddSongToPlaylistAsync(playlist.Id, song.Id);
                }
            }
        }

        return playlist;
    }
}
