// 配置管理器 —— 以 JSON 格式读写应用配置（音量、主题、监听文件夹等）
using System.IO;
using System.Text.Json;
using MusicPlayer.Models;

namespace MusicPlayer.Helpers;

public static class ConfigManager
{
    // 配置文件路径：%LocalAppData%/MusicPlayer/config.json
    private static readonly string ConfigPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "MusicPlayer", "config.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,              // 格式化输出，便于人工阅读
        PropertyNameCaseInsensitive = true // 忽略属性名大小写
    };

    // 从文件加载配置，不存在则返回默认值
    public static AppConfig Load()
    {
        try
        {
            if (File.Exists(ConfigPath))
            {
                var json = File.ReadAllText(ConfigPath);
                return JsonSerializer.Deserialize<AppConfig>(json, JsonOptions) ?? new AppConfig();
            }
        }
        catch { }
        return new AppConfig();
    }

    // 保存配置到文件
    public static void Save(AppConfig config)
    {
        try
        {
            var folder = Path.GetDirectoryName(ConfigPath);
            if (!string.IsNullOrEmpty(folder))
                Directory.CreateDirectory(folder);

            var json = JsonSerializer.Serialize(config, JsonOptions);
            File.WriteAllText(ConfigPath, json);
        }
        catch { }
    }
}
