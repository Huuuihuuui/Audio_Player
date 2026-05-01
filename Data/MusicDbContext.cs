// 数据库上下文 —— EF Core 与 SQLite 之间的桥梁，管理所有实体集的 CRUD 操作
using System.IO;
using Microsoft.EntityFrameworkCore;
using MusicPlayer.Models;

namespace MusicPlayer.Data;

public class MusicDbContext : DbContext
{
    // 歌曲表
    public DbSet<Song> Songs => Set<Song>();

    // 歌单表
    public DbSet<Playlist> Playlists => Set<Playlist>();

    // 歌单-歌曲关联表（多对多中间表）
    public DbSet<PlaylistSong> PlaylistSongs => Set<PlaylistSong>();

    // 播放历史表
    public DbSet<PlayHistory> PlayHistories => Set<PlayHistory>();

    // 数据库文件路径
    private readonly string _dbPath = string.Empty;

    // 无参构造函数：自动在用户 AppData 目录创建数据库文件
    public MusicDbContext()
    {
        var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MusicPlayer");
        Directory.CreateDirectory(folder);
        _dbPath = Path.Combine(folder, "musicplayer.db");
    }

    // 带参数构造函数：允许注入自定义配置（预留扩展）
    public MusicDbContext(DbContextOptions<MusicDbContext> options) : base(options) { }

    // 配置数据库提供程序
    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        if (!optionsBuilder.IsConfigured)
        {
            optionsBuilder.UseSqlite($"Data Source={_dbPath}");
        }
    }

    // 配置实体关系、联合主键、级联删除和唯一索引
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // PlaylistSong 联合主键
        modelBuilder.Entity<PlaylistSong>()
            .HasKey(ps => new { ps.PlaylistId, ps.SongId });

        // PlaylistSong → Playlist：级联删除（删除歌单时同时删除关联记录）
        modelBuilder.Entity<PlaylistSong>()
            .HasOne(ps => ps.Playlist)
            .WithMany(p => p.PlaylistSongs)
            .HasForeignKey(ps => ps.PlaylistId)
            .OnDelete(DeleteBehavior.Cascade);

        // PlaylistSong → Song：级联删除（删除歌曲时同时删除关联记录）
        modelBuilder.Entity<PlaylistSong>()
            .HasOne(ps => ps.Song)
            .WithMany(s => s.PlaylistSongs)
            .HasForeignKey(ps => ps.SongId)
            .OnDelete(DeleteBehavior.Cascade);

        // PlayHistory → Song：级联删除（删除歌曲时同时删除播放记录）
        modelBuilder.Entity<PlayHistory>()
            .HasOne(ph => ph.Song)
            .WithMany(s => s.PlayHistories)
            .HasForeignKey(ph => ph.SongId)
            .OnDelete(DeleteBehavior.Cascade);

        // 文件路径唯一索引，防止重复入库
        modelBuilder.Entity<Song>()
            .HasIndex(s => s.FilePath)
            .IsUnique();
    }
}
