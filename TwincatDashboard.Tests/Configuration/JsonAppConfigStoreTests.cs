using Microsoft.Extensions.Logging.Abstractions;

using TwincatDashboard.Models;
using TwincatDashboard.Services.Configuration;
using TwincatDashboard.Tests.TestHelpers;

namespace TwincatDashboard.Tests.Configuration;

public sealed class JsonAppConfigStoreTests {
  private sealed class TestPaths(string root) : IAppPaths {
    public string AppName => "TestApp";
    public string AppDataDirectory => root;
    public string ConfigFilePath => System.IO.Path.Combine(root, "config.json");
    public string LogDirectory => System.IO.Path.Combine(root, "log");
    public string LogFilePath => System.IO.Path.Combine(LogDirectory, "log.txt");
    public string TempDirectory => System.IO.Path.Combine(root, "tmp");
  }

  [Fact]
  public async Task LoadAsync_WhenFileMissing_DoesNotThrowAndKeepsDefaultConfig() {
    using var tmp = new TempDirectory();
    var store = new JsonAppConfigStore(new TestPaths(tmp.Path), NullLogger<JsonAppConfigStore>.Instance);

    await store.LoadAsync();

    Assert.NotNull(store.Current);
    Assert.NotNull(store.Current.AdsConfig);
    Assert.NotNull(store.Current.LogConfig);
  }

  [Fact]
  public async Task SaveAsync_ThenLoadAsync_RoundTripsConfig() {
    using var tmp = new TempDirectory();
    var paths = new TestPaths(tmp.Path);

    var store = new JsonAppConfigStore(paths, NullLogger<JsonAppConfigStore>.Instance);
    store.Current.AdsConfig = new AdsConfig { NetId = "1.2.3.4.5.6", PortId = 999 };
    store.Current.LogConfig = new LogConfig {
      FolderName = "C:\\logs",
      FileName = "mylog",
      QuickLogPeriod = 7,
      SlowLogPeriod = 1234,
      ReadNamespace = ["MAIN"],
      FileType = ["csv"]
    };

    await store.SaveAsync();
    Assert.True(File.Exists(paths.ConfigFilePath));

    var reloaded = new JsonAppConfigStore(paths, NullLogger<JsonAppConfigStore>.Instance);
    await reloaded.LoadAsync();

    Assert.Equal("1.2.3.4.5.6", reloaded.Current.AdsConfig.NetId);
    Assert.Equal(999, reloaded.Current.AdsConfig.PortId);
    Assert.Equal("C:\\logs", reloaded.Current.LogConfig.FolderName);
    Assert.Equal("mylog", reloaded.Current.LogConfig.FileName);
    Assert.Equal(7, reloaded.Current.LogConfig.QuickLogPeriod);
    Assert.Equal(1234, reloaded.Current.LogConfig.SlowLogPeriod);
    Assert.Equal(["MAIN"], reloaded.Current.LogConfig.ReadNamespace);
    Assert.Equal(["csv"], reloaded.Current.LogConfig.FileType);
  }
}
