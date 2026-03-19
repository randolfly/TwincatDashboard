using MatFileIO;

using Microsoft.Extensions.Logging;

using System.Buffers;
using System.Buffers.Text;
using System.IO;
using System.Text;

using TwincatDashboard.Services.Configuration;
using TwincatDashboard.Utils;

namespace TwincatDashboard.Services;

public class LogDataService {
  private static int BufferCapacity => 1000;

  private readonly IAppPaths _paths;
  private readonly ILogger<LogDataService> _logger;
  private readonly ILoggerFactory _loggerFactory;

  public LogDataService(IAppPaths paths, ILogger<LogDataService> logger, ILoggerFactory loggerFactory) {
    _paths = paths;
    _logger = logger;
    _loggerFactory = loggerFactory;

    Directory.CreateDirectory(_paths.TempDirectory);
    _logger.LogDebug("Log temp directory: {TempDirectory}", _paths.TempDirectory);
  }

  public Dictionary<string, List<double>> SlowLogDict { get; } = [];
  public Dictionary<string, LogDataChannel> QuickLogDict { get; } = [];

  public void AddChannel(string channelName) {
    _ = GetOrAddChannel(channelName);
  }

  public LogDataChannel? GetOrAddChannel(string channelName) {
    if (string.IsNullOrWhiteSpace(channelName))
      return null;

    if (QuickLogDict.ContainsKey(channelName))
      return QuickLogDict[channelName];

    var channelLogger = _loggerFactory.CreateLogger($"LogDataChannel[{channelName}]");
    var channel = new LogDataChannel(BufferCapacity, channelName, _paths.TempDirectory, channelLogger);
    QuickLogDict.Add(channelName, channel);
    return channel;
  }

  public void RemoveAllChannels() {
    QuickLogDict.Values.ToList().ForEach(channel => channel.Dispose());
    QuickLogDict.Clear();
  }

  /// <summary>
  /// Hot path (ADS 1ms cyclic): keep this sync and allocation-free.
  /// </summary>
  public void AddData(string channelName, double data) {
    if (QuickLogDict.TryGetValue(channelName, out var value))
      value.Add(data);
  }

  public Task AddDataAsync(string channelName, double data) {
    AddData(channelName, data);
    return Task.CompletedTask;
  }

  /// <summary>
  /// Ensure all pending buffered writes are flushed to temp files before export/load.
  /// </summary>
  public async Task FlushAllChannelsAsync() {
    foreach (var channel in QuickLogDict.Values)
      await channel.FlushAsync();
  }

  public async Task<Dictionary<string, double[]>> LoadAllChannelsAsync() {
    var resultDict = new Dictionary<string, double[]>();
    foreach (var channel in QuickLogDict) {
      await channel.Value.LoadFromFileByArrayPoolAsync();
      resultDict.Add(channel.Key, channel.Value.LogData);
    }

    return resultDict;
  }

  public void RegisterSlowLog(string channelName) {
    SlowLogDict.Add(channelName, []);
  }

  public void AddSlowLogData(string channelName, double data) {
    if (SlowLogDict.TryGetValue(channelName, out var value)) value.Add(data);
  }

  public void RemoveAllSlowLog() {
    foreach (var channel in SlowLogDict) channel.Value.Clear();

    SlowLogDict.Clear();
  }

  /// <summary>
  ///     export data to file
  /// </summary>
  /// <param name="dataSrc"></param>
  /// <param name="fileName">export file full name, doesn't contain suffix, such as "c:/FOLDER/aaa"</param>
  /// ///
  /// <param name="exportTypes"></param>
  /// <param name="dataLength">actual data length</param>
  /// <returns></returns>
  public async Task ExportDataAsync(
      Dictionary<string, double[]> dataSrc,
      string fileName,
      List<string> exportTypes,
      int dataLength
  ) {
    if (exportTypes.Contains("csv")) {
      await using var fileStream = new FileStream(
          fileName + ".csv",
          FileMode.Create,
          FileAccess.Write,
          FileShare.None,
          64 * 1024,
          true
      );

      var valueBuffer = ArrayPool<byte>.Shared.Rent(256);
      var rowBuffer = ArrayPool<byte>.Shared.Rent(4096);

      try {
        await WriteHeaderAsync();

        var rowCount = dataLength;
        for (var i = 0; i < rowCount; i++) {
          var rowLength = 0;
          var colIndex = 0;

          foreach (var channel in dataSrc) {
            if (colIndex > 0)
              AppendByte(ref rowLength, (byte)',');

            if (Utf8Formatter.TryFormat(channel.Value[i], valueBuffer, out var bytesWritten, new StandardFormat('G', 17)))
              AppendBytes(ref rowLength, valueBuffer.AsSpan(0, bytesWritten));

            colIndex++;
          }

          AppendByte(ref rowLength, (byte)'\n');
          await fileStream.WriteAsync(rowBuffer.AsMemory(0, rowLength));
        }
      } finally {
        ArrayPool<byte>.Shared.Return(valueBuffer);
        ArrayPool<byte>.Shared.Return(rowBuffer);
      }

      async Task WriteHeaderAsync() {
        var rowLength = 0;
        var colIndex = 0;

        foreach (var key in dataSrc.Keys) {
          if (colIndex > 0)
            AppendByte(ref rowLength, (byte)',');

          AppendUtf8String(ref rowLength, key);
          colIndex++;
        }

        AppendByte(ref rowLength, (byte)'\n');
        await fileStream.WriteAsync(rowBuffer.AsMemory(0, rowLength));
      }

      void AppendByte(ref int length, byte value) {
        EnsureRowCapacity(length + 1, length);
        rowBuffer[length++] = value;
      }

      void AppendBytes(ref int length, ReadOnlySpan<byte> bytes) {
        EnsureRowCapacity(length + bytes.Length, length);
        bytes.CopyTo(rowBuffer.AsSpan(length));
        length += bytes.Length;
      }

      void AppendUtf8String(ref int length, string text) {
        var byteCount = Encoding.UTF8.GetByteCount(text);
        EnsureRowCapacity(length + byteCount, length);
        length += Encoding.UTF8.GetBytes(text, rowBuffer.AsSpan(length));
      }

      void EnsureRowCapacity(int required, int currentLength) {
        if (required <= rowBuffer.Length)
          return;

        var newSize = Math.Max(required, rowBuffer.Length * 2);
        var newBuffer = ArrayPool<byte>.Shared.Rent(newSize);
        rowBuffer.AsSpan(0, currentLength).CopyTo(newBuffer);
        ArrayPool<byte>.Shared.Return(rowBuffer);
        rowBuffer = newBuffer;
      }
    }

    if (exportTypes.Contains("mat")) {
      //var exportMatDict = new Dictionary<string, Matrix<double>>();
      //foreach (var keyValuePair in dataSrc) {
      //    // TODO: The Matrix.Build.Dense method require an array instead of a Span, which leads to further array copy
      //    //var arraySlice = new ReadOnlySpan<double>(
      //    //    keyValuePair.Value, 0, dataLength);
      //    exportMatDict.Add(
      //        FormatNameForMatFile(keyValuePair.Key),
      //        Matrix<double>.Build.Dense(keyValuePair.Value.Length, 1, keyValuePair.Value)
      //    );
      //    DenseVector.Build.DenseOfArray(keyValuePair.Value);
      //}

      //await Task.Run(() =>
      //    MathNet.Numerics.Data.Matlab.MatlabWriter.Write(fileName + ".mat", exportMatDict)
      //);
      await using var mw = new MatWriter(fileName + ".mat");

      foreach (var keyValuePair in dataSrc) {
        var data = new Memory<double>(keyValuePair.Value, 0, dataLength);
        await mw.WriteArrayAsync(FormatNameForMatFile(keyValuePair.Key), data);
      }
    }

    static string FormatNameForMatFile(string symbolName) {
      return symbolName
          .Replace("TwinCAT_SystemInfoVarList._TaskInfo[1].", "Task")
          .Replace(".", "_")
          .Replace("[", "_")
          .Replace("]", "_");
    }
  }



  public void DeleteTmpFiles() {
    QuickLogDict.Values.ToList().ForEach(channel => channel.DeleteTmpFile());
  }
}