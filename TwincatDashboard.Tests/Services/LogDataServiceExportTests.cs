using Microsoft.Extensions.Logging.Abstractions;

using TwincatDashboard.Services;
using TwincatDashboard.Services.Configuration;
using TwincatDashboard.Tests.TestHelpers;

namespace TwincatDashboard.Tests.Services;

public sealed class LogDataServiceExportTests {
  private sealed class TestPaths(string root) : IAppPaths {
    public string AppName => "TestApp";
    public string AppDataDirectory => root;
    public string ConfigFilePath => System.IO.Path.Combine(root, "config.json");
    public string LogDirectory => System.IO.Path.Combine(root, "log");
    public string LogFilePath => System.IO.Path.Combine(LogDirectory, "log.txt");
    public string TempDirectory => System.IO.Path.Combine(root, "tmp");
  }

  [Fact]
  public async Task ExportDataAsync_Csv_WritesHeaderAndRows() {
    using var tmp = new TempDirectory();
    var service = new LogDataService(new TestPaths(tmp.Path), NullLogger<LogDataService>.Instance, NullLoggerFactory.Instance);

    var data = new Dictionary<string, double[]> {
      ["A"] = [1, 2, 3],
      ["B"] = [4, 5, 6]
    };

    var fileBase = System.IO.Path.Combine(tmp.Path, "export");
    await service.ExportDataAsync(data, fileBase, exportTypes: ["csv"], dataLength: 3);

    var csvPath = fileBase + ".csv";
    Assert.True(File.Exists(csvPath));

    var lines = await File.ReadAllLinesAsync(csvPath);
    Assert.Equal("A,B", lines[0]);
    Assert.Equal(4, lines.Length); // header + 3 rows
  }
}

