using System;
using System.IO;
using System.Text.Json;

namespace RUSTool.Settings;

/// <summary>
/// JSON 文件实现：默认落在用户配置目录 <c>~/.config/RUSTool/settings.json</c>
/// （Windows 为 <c>%APPDATA%</c>）。
///
/// <para>
/// <b>读写一律容错</b>：文件不存在 → 默认值；JSON 损坏 / 权限不足 → 默认值 + 保存失败静默返回 false。
/// 设置文件不是关键数据，宁可回退默认也不能让应用因为一个坏 JSON 起不来。
/// </para>
/// </summary>
public sealed class JsonSettingsStore : ISettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    public string Location { get; }

    public JsonSettingsStore(string? path = null)
        => Location = path ?? DefaultPath();

    /// <summary>默认配置路径：用户配置目录下的 <c>RUSTool/settings.json</c>。</summary>
    public static string DefaultPath()
    {
        string root = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (string.IsNullOrEmpty(root))
            root = AppContext.BaseDirectory; // 极端环境兜底：放到程序目录下
        return Path.Combine(root, "RUSTool", "settings.json");
    }

    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(Location))
                return new AppSettings();

            string json = File.ReadAllText(Location);
            return JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
        }
        catch (Exception)
        {
            // 损坏 / 无权限：回退默认值（不抛）。
            return new AppSettings();
        }
    }

    public bool Save(AppSettings settings)
    {
        try
        {
            string? dir = Path.GetDirectoryName(Location);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            // 先写临时文件再替换，避免写一半被中断留下坏文件。
            string tmp = Location + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(settings, JsonOptions));
            File.Move(tmp, Location, overwrite: true);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
