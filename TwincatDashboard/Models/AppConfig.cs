using System.IO;
using System.Reflection;
using System.Text.Json.Serialization;

using TwinCAT.Ads;

using TwincatDashboard.Constants;

namespace TwincatDashboard.Models;

public class AppConfig {
    public AdsConfig AdsConfig { get; set; } = new();
    public LogConfig LogConfig { get; set; } = new();

    #region 配置文件存储路径

    public static string AppName => Assembly.GetCallingAssembly().FullName!.Split(',')[0];

    public static string FolderName =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            AppName
        );

    public static string FileName => AppName + ".json";
    public static string ConfigFileFullName => Path.Combine(FolderName, FileName);

    public static string AppLogFileFullName =>
        Path.Combine(FolderName, "log", AppName + "_log_" + ".txt");
    #endregion
}

public class AdsConfig {
    public string NetId { get; set; } = AmsNetId.Local.ToString();
    public int PortId { get; set; } = 851;
}

public class LogConfig {
    // log symbol period in ms
    public int QuickLogPeriod { get; set; } = 2;
    public int SlowLogPeriod { get; set; } = 5000;

    public List<string> FileType { get; set; } = AppConstants.SupportedLogFileTypes;
    public List<string> LogSymbols { get; set; } = [];
    public List<string> PlotSymbols { get; set; } = [];
    public List<string> QuickLogSymbols { get; set; } = [];

    public string FolderName { get; set; } =
        Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
    public string FileName { get; set; } = "log";

    [JsonIgnore]
    public string TempFileFullName => Path.Combine(FolderName, FileName);

    [JsonIgnore]
    public string QuickLogFileFullName {
        get {
            var datetime = DateTime.Now;
            var fileName =
                FileName
                + "_quick_"
                + QuickLogPeriod
                + "ms"
                + "_"
                + datetime.ToString("yyyyMMddHHmmss");
            return Path.Combine(FolderName, fileName);
        }
    }

    [JsonIgnore]
    public string SlowLogFileFullName {
        get {
            var datetime = DateTime.Now;
            var fileName =
                FileName
                + "_slow_"
                + SlowLogPeriod
                + "ms"
                + "_"
                + datetime.ToString("yyyyMMddHHmmss");
            return Path.Combine(FolderName, fileName);
        }
    }
}
