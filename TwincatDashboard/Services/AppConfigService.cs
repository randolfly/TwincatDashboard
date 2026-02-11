using System.Diagnostics;
using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using TwincatDashboard.Models;

namespace TwincatDashboard.Services;

public static class AppConfigService
{
    private static readonly JsonSerializerOptions JsonSerializerOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static AppConfig AppConfig { get; set; } = new();

    /// <summary>
    ///     从json文件加载配置，只应该在初始化Service时调用一次（否则其余地方的AppConfig读取不到最新的配置）【或者给AppConfig下的子config DeepClone】
    /// </summary>
    /// <param name="configFileFullName">配置文件地址</param>
    public static void LoadConfig(string configFileFullName)
    {
        Debug.WriteLine($"LoadConfig: {configFileFullName}");
        if (!File.Exists(configFileFullName)) return;
        using var fs = new FileStream(configFileFullName, FileMode.Open);
        AppConfig = JsonSerializer.Deserialize<AppConfig>(fs, JsonSerializerOptions) ?? new AppConfig();
    }

    /// <summary>
    ///     存储配置到json文件
    /// </summary>
    /// <param name="configFileFullName">配置文件地址</param>
    public static void SaveConfig(string configFileFullName)
    {
        Debug.WriteLine($"SaveConfig: {configFileFullName}");
        if (!Directory.Exists(Path.GetDirectoryName(configFileFullName)))
            Directory.CreateDirectory(Path.GetDirectoryName(configFileFullName)!);
        using var fs = new FileStream(configFileFullName, FileMode.Create);
        JsonSerializer.Serialize(fs, AppConfig, JsonSerializerOptions);
    }
}