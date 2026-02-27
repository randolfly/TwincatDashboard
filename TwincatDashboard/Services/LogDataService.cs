using Serilog;

using System.Buffers;
using System.Buffers.Text;
using System.IO;
using System.Text;

using TwincatDashboard.Models;
using TwincatDashboard.Utils;

namespace TwincatDashboard.Services;

public class LogDataService {
  private static int BufferCapacity => 1000;

  public Dictionary<string, List<double>> SlowLogDict { get; } = [];
  public Dictionary<string, LogDataChannel> QuickLogDict { get; } = [];

  public void AddChannel(string channelName) {
    QuickLogDict.Add(channelName, new LogDataChannel(BufferCapacity, channelName));
  }

  public void RemoveAllChannels() {
    QuickLogDict.Clear();
  }

  public async Task AddDataAsync(string channelName, double data) {
    if (QuickLogDict.TryGetValue(channelName, out var value)) await value.AddAsync(data);
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
      using var fs = new FileStream(fileName + ".mat",
          FileMode.Create,
          FileAccess.Write);
      MatlabWriter.WriteMatFileHeader(fs);
      foreach (var keyValuePair in dataSrc) {
        var data = new ReadOnlySpan<double>(keyValuePair.Value);
        MatlabWriter.WriteArray(
            fs,
            FormatNameForMatFile(keyValuePair.Key),
            data,
            dataLength);
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

public class LogDataChannel(int bufferCapacity, string channelName) : IDisposable {
  // storage tmp data for logging(default data type is double)
  private readonly CircularBuffer<double> _channelBuffer = new(bufferCapacity);
  private string Name { get; } = channelName;
  public string? Description { get; set; }
  public int BufferCapacity { get; init; } = bufferCapacity;
  public int DataLength { get; private set; }

  private static string LogDataTempFolder {
    get {
      var path = Path.Combine(AppConfig.FolderName, AppConfig.FolderName, "tmp/");
      if (!Directory.Exists(path)) Directory.CreateDirectory(path);

      return path;
    }
  }

  private string FilePath => Path.Combine(LogDataTempFolder, "_" + Name + ".csv");

  // Use ArrayPool to rent and return arrays for performance optimization
  private static ArrayPool<double> ArrayPool => ArrayPool<double>.Shared;
  public double[] LogData { get; private set; } = ArrayPool.Rent(0);

  public async Task AddAsync(double data) {
    _channelBuffer.Add(data);
    DataLength++;
    if (_channelBuffer.Size * 2 >= _channelBuffer.Capacity) {
      Log.Information($"Buffer is half size, save to file: {FilePath}");
      var dataSrc = _channelBuffer.RemoveRange(_channelBuffer.Size);
      await SaveToFileAsync(dataSrc, FilePath);
    }
  }

  private static async Task SaveToFileAsync(ArraySegment<double> array, string filePath) {
    await using var fileStream = new FileStream(
        filePath,
        FileMode.Append,
        FileAccess.Write,
        FileShare.None,
        64 * 512,
        true
    );

    var buffer = ArrayPool<byte>.Shared.Rent(256);
    try {
      foreach (var value in array) {
        if (!Utf8Formatter.TryFormat(value, buffer, out var bytesWritten, new StandardFormat('G', 17)))
          continue;

        buffer[bytesWritten++] = (byte)'\n';
        await fileStream.WriteAsync(buffer.AsMemory(0, bytesWritten));
      }
    } finally {
      ArrayPool<byte>.Shared.Return(buffer);
    }
  }

  public async Task LoadFromFileByArrayPoolAsync() {
    // if file not exists, return empty array pool
    if (!File.Exists(FilePath)) {
      LogData = ArrayPool.Rent(0);
      return;
    }

    LogData = ArrayPool.Rent(DataLength);

    await using var fileStream = new FileStream(
        FilePath,
        FileMode.Open,
        FileAccess.Read,
        FileShare.Read,
        64 * 1024,
        true
    );

    var buffer = ArrayPool<byte>.Shared.Rent(64 * 1024);
    byte[] lineBuffer = ArrayPool<byte>.Shared.Rent(256);
    var lineLength = 0;
    var index = 0;
    var stop = false;

    try {
      int bytesRead;
      while (!stop && (bytesRead = await fileStream.ReadAsync(buffer.AsMemory(0, buffer.Length))) > 0) {
        var span = buffer.AsSpan(0, bytesRead);
        var start = 0;

        while (start < span.Length && !stop) {
          var newlineIndex = span.Slice(start).IndexOf((byte)'\n');
          // if bytes are not aligned, the rest data are stored into lineBuffer until next read
          if (newlineIndex < 0) {
            AppendToLineBuffer(span.Slice(start));
            break;
          }

          var chunk = span.Slice(start, newlineIndex);
          AppendToLineBuffer(chunk);
          ProcessLine();
          start += newlineIndex + 1;
        }
      }

      if (!stop && lineLength > 0)
        ProcessLine();
    } finally {
      ArrayPool<byte>.Shared.Return(buffer);
      ArrayPool<byte>.Shared.Return(lineBuffer);
    }

    // update DataLength if actual data length is less than expected(file IO latency may cause this)
    if (index <= DataLength)
      DataLength = index;

    if (DataLength > 0) {
      for (var i = DataLength; i < LogData.Length; i++)
        LogData[i] = LogData[DataLength - 1]; // fill the rest with last value
    }

    Log.Information(
        "{File} ideal data length: {DataLength}, actual data length: {ActualLength}",
        FilePath,
        LogData.Length,
        index
    );

    Log.Information("Retrieve all data from file {FilePath}", FilePath);

    void AppendToLineBuffer(ReadOnlySpan<byte> chunk) {
      EnsureLineBufferCapacity(lineLength + chunk.Length);
      chunk.CopyTo(lineBuffer.AsSpan(lineLength));
      lineLength += chunk.Length;
    }

    void EnsureLineBufferCapacity(int required) {
      if (required <= lineBuffer.Length)
        return;

      var newSize = Math.Max(required, lineBuffer.Length * 2);
      var newBuffer = ArrayPool<byte>.Shared.Rent(newSize);
      lineBuffer.AsSpan(0, lineLength).CopyTo(newBuffer);
      ArrayPool<byte>.Shared.Return(lineBuffer);
      lineBuffer = newBuffer;
    }

    void ProcessLine() {
      if (index >= LogData.Length) {
        stop = true;
        return;
      }

      var lineSpan = lineBuffer.AsSpan(0, lineLength);
      if (lineSpan.Length > 0 && lineSpan[^1] == (byte)'\r')
        lineSpan = lineSpan[..^1];

      if (Utf8Parser.TryParse(lineSpan, out double value, out var consumed) && consumed == lineSpan.Length) {
        LogData[index] = value;
      } else {
        Log.Warning(
            "Failed to parse line: {Line} of {File}",
            Encoding.UTF8.GetString(lineSpan),
            FilePath
        );
        LogData[index] = 0;
      }

      index++;
      lineLength = 0;
    }
  }

  public void DeleteTmpFile() {
    if (File.Exists(FilePath)) {
      Log.Information("Delete tmp log file: {FilePath}", FilePath);
      File.Delete(FilePath);
    }
  }

  public void Dispose() {
    if (LogData is not null && LogData.Length > 0) {
      Log.Information("Return {Channel} LogData of size: {Size}", Name, LogData.Length);
      ArrayPool.Return(LogData);
      LogData = [];
    }

    _channelBuffer.ReturnBufferToArrayPool();
  }
}