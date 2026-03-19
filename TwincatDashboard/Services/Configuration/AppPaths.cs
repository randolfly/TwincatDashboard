using System.IO;
using System.Reflection;

namespace TwincatDashboard.Services.Configuration;

public sealed class AppPaths : IAppPaths {
  private readonly string _appName;
  private readonly string _appDataDirectory;

  public AppPaths()
    : this(
      appName: Assembly.GetEntryAssembly()?.GetName().Name ?? "TwincatDashboard",
      appDataRoot: Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
    ) {
  }

  public AppPaths(string appName, string appDataRoot) {
    _appName = string.IsNullOrWhiteSpace(appName) ? "TwincatDashboard" : appName;
    _appDataDirectory = Path.Combine(appDataRoot, _appName);
  }

  public string AppName => _appName;
  public string AppDataDirectory => _appDataDirectory;

  public string ConfigFilePath => Path.Combine(AppDataDirectory, $"{AppName}.json");

  public string LogDirectory => Path.Combine(AppDataDirectory, "log");
  public string LogFilePath => Path.Combine(LogDirectory, $"{AppName}_log_.txt");

  public string TempDirectory => Path.Combine(AppDataDirectory, "tmp");
}

