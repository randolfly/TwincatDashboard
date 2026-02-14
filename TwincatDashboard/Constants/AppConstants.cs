namespace TwincatDashboard.Constants;

public static class AppConstants {
  public const string AdsRouteXmlPath = @"C:\TwinCAT\3.1\Target\StaticRoutes.xml";
  public static readonly List<string> SupportedLogFileTypes = ["csv", "mat"];
}

public static class AdsConstants {
  public const string TaskCycleTimeName = "TwinCAT_SystemInfoVarList._TaskInfo[1].CycleTime";
  public const string TaskCycleCountName = "TwinCAT_SystemInfoVarList._TaskInfo[1].CycleCount";
}

public static class DebugCategory {
  public const string Ads = "ADS";
}