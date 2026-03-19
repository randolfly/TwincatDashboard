using Microsoft.Extensions.Logging.Abstractions;

using TwincatDashboard.Services;
using TwincatDashboard.Tests.TestHelpers;

namespace TwincatDashboard.Tests.Services;

public sealed class LogDataChannelTests {
  [Fact]
  public async Task LoadFromFileByArrayPoolAsync_WhenFileHasFewerLines_UpdatesDataLengthAndFillsRest() {
    using var tmp = new TempDirectory();

    // Small capacity so we flush quickly and leave some data in memory only.
    var channel = new LogDataChannel(
      bufferCapacity: 10,
      channelName: "ch1",
      tempDirectory: tmp.Path,
      logger: NullLogger.Instance);

    for (var i = 0; i < 12; i++)
      await channel.AddAsync(i);

    await channel.LoadFromFileByArrayPoolAsync();

    var filePath = System.IO.Path.Combine(tmp.Path, "_ch1.csv");
    var fileLineCount = File.ReadAllLines(filePath).Length;

    // LoadFromFileByArrayPoolAsync should update DataLength to match the file content
    // (it rents an array sized to the "ideal" DataLength, but may adjust down).
    Assert.Equal(fileLineCount, channel.DataLength);
    Assert.Equal(0d, channel.LogData[0]);

    // Values beyond DataLength are filled with the last parsed value.
    var last = channel.LogData[channel.DataLength - 1];
    for (var i = channel.DataLength; i < channel.LogData.Length; i++)
      Assert.Equal(last, channel.LogData[i]);

    channel.Dispose();
  }
}
