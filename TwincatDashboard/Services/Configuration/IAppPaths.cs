namespace TwincatDashboard.Services.Configuration;

public interface IAppPaths {
  string AppName { get; }
  string AppDataDirectory { get; }

  string ConfigFilePath { get; }

  string LogDirectory { get; }
  string LogFilePath { get; }

  string TempDirectory { get; }
}

