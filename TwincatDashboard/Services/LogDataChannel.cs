using Microsoft.Extensions.Logging;

using System.Buffers;
using System.Buffers.Text;
using System.IO;
using System.Text;

using TwincatDashboard.Utils;

namespace TwincatDashboard.Services;

public sealed class LogDataChannel : IDisposable {
  private readonly CircularBuffer<double> _channelBuffer;
  private readonly string _name;
  private readonly string _filePath;
  private readonly FileStream _fileStream;
  private readonly ILogger _logger;
  private readonly Lock _gate = new();

  // Chunks that are ready to be appended to the temp file.
  // We must copy out of the circular buffer before writing asynchronously, otherwise the buffer may overwrite.
  private readonly Queue<(double[] Buffer, int Length)> _flushQueue = new();
  private volatile Task? _flushWorker;
  private int _flushWorkerRunning;
  private bool _disposed;
  private readonly byte[] _formatScratch = ArrayPool<byte>.Shared.Rent(256);
  private readonly byte[] _writeBuffer = ArrayPool<byte>.Shared.Rent(64 * 1024);

  public LogDataChannel(int bufferCapacity, string channelName, string tempDirectory, ILogger logger) {
    if (bufferCapacity <= 0)
      throw new ArgumentOutOfRangeException(nameof(bufferCapacity));

    if (string.IsNullOrWhiteSpace(channelName))
      throw new ArgumentException("Channel name is required.", nameof(channelName));

    _channelBuffer = new CircularBuffer<double>(bufferCapacity);
    _name = channelName;
    BufferCapacity = bufferCapacity;
    _logger = logger;

    Directory.CreateDirectory(tempDirectory);
    _filePath = Path.Combine(tempDirectory, "_" + _name + ".csv");
    _fileStream = new FileStream(
        _filePath,
        FileMode.Create,
        FileAccess.Write,
        FileShare.Read,
        64 * 1024,
        FileOptions.Asynchronous | FileOptions.SequentialScan
    );
  }

  public string? Description { get; set; }
  public int BufferCapacity { get; }
  public int DataLength { get; private set; }

  // Use ArrayPool to rent and return arrays for performance optimization
  private static ArrayPool<double> ArrayPool => ArrayPool<double>.Shared;
  public double[] LogData { get; private set; } = ArrayPool.Rent(0);

  /// <summary>
  /// Hot path: sync + no Task allocation per sample.
  /// </summary>
  public void Add(double data) {
    lock (_gate) {
      if (_disposed) return;

      _channelBuffer.Add(data);
      DataLength++;

      if (_channelBuffer.Size * 2 < _channelBuffer.Capacity)
        return;

      _logger.LogDebug("Buffer is half size, queue flush to file: {FilePath}", _filePath);
      EnqueueFlushChunk_NoLock();
    }

    StartFlushWorkerIfNeeded();
  }

  public Task AddAsync(double data) {
    Add(data);
    return Task.CompletedTask;
  }

  public async Task FlushAsync() {
    lock (_gate) {
      if (_disposed) return;
      if (_channelBuffer.Size > 0)
        EnqueueFlushChunk_NoLock();
    }

    StartFlushWorkerIfNeeded();

    while (true) {
      var worker = _flushWorker;
      if (worker is not null)
        await worker;

      lock (_gate) {
        if (_flushQueue.Count == 0 && _channelBuffer.Size == 0 && _flushWorkerRunning == 0)
          return;

        if (_channelBuffer.Size > 0)
          EnqueueFlushChunk_NoLock();
      }

      StartFlushWorkerIfNeeded();
    }
  }

  private void EnqueueFlushChunk_NoLock() {
    var dataSrc = _channelBuffer.RemoveRange(_channelBuffer.Size);
    var total = dataSrc.First.Length + dataSrc.Second.Length;
    if (total <= 0)
      return;

    var rented = ArrayPool.Rent(total);
    dataSrc.First.Span.CopyTo(rented);
    dataSrc.Second.Span.CopyTo(rented.AsSpan(dataSrc.First.Length));
    _flushQueue.Enqueue((rented, total));
  }

  private void StartFlushWorkerIfNeeded() {
    if (Interlocked.CompareExchange(ref _flushWorkerRunning, 1, 0) != 0)
      return;

    _flushWorker = Task.Run(FlushWorkerAsync);
  }

  private async Task FlushWorkerAsync() {
    try {
      while (true) {
        (double[] Buffer, int Length) chunk;

        lock (_gate) {
          if (_flushQueue.Count == 0)
            break;

          chunk = _flushQueue.Dequeue();
        }

        try {
          await SaveToFileAsync(chunk.Buffer, chunk.Length);
        } finally {
          ArrayPool.Return(chunk.Buffer);
        }
      }
    } finally {
      Interlocked.Exchange(ref _flushWorkerRunning, 0);

      bool hasMore;
      lock (_gate) {
        hasMore = _flushQueue.Count > 0;
      }

      if (hasMore)
        StartFlushWorkerIfNeeded();
    }
  }

  private async Task SaveToFileAsync(double[] data, int length) {
    var buffered = 0;

    for (var i = 0; i < length; i++) {
      var value = data[i];
      if (!Utf8Formatter.TryFormat(value, _formatScratch, out var bytesWritten, new StandardFormat('G', 17)))
        continue;

      _formatScratch[bytesWritten++] = (byte)'\n';

      if (buffered + bytesWritten > _writeBuffer.Length) {
        await _fileStream.WriteAsync(_writeBuffer.AsMemory(0, buffered));
        buffered = 0;
      }

      _formatScratch.AsSpan(0, bytesWritten).CopyTo(_writeBuffer.AsSpan(buffered));
      buffered += bytesWritten;
    }

    if (buffered > 0)
      await _fileStream.WriteAsync(_writeBuffer.AsMemory(0, buffered));
  }

  public async Task LoadFromFileByArrayPoolAsync() {
    // Make sure all buffered data is on disk before reading.
    await FlushAsync();
    await _fileStream.FlushAsync();

    // if file not exists, return empty array pool
    if (!File.Exists(_filePath)) {
      LogData = ArrayPool.Rent(0);
      return;
    }

    LogData = ArrayPool.Rent(DataLength);

    await using var fileStream = new FileStream(
        _filePath,
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

    _logger.LogInformation(
        "{File} ideal data length: {DataLength}, actual data length: {ActualLength}",
        _filePath,
        LogData.Length,
        index
    );

    _logger.LogInformation("Retrieve all data from file {FilePath}", _filePath);

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
        _logger.LogWarning(
            "Failed to parse line: {Line} of {File}",
            Encoding.UTF8.GetString(lineSpan),
            _filePath
        );
        LogData[index] = 0;
      }

      index++;
      lineLength = 0;
    }
  }

  public void DeleteTmpFile() {
    try {
      _fileStream.Flush();
    } catch {
      // ignored
    }

    try {
      _fileStream.Dispose();
    } catch {
      // ignored
    }

    if (File.Exists(_filePath)) {
      _logger.LogInformation("Delete tmp log file: {FilePath}", _filePath);
      File.Delete(_filePath);
    }
  }

  public void Dispose() {
    lock (_gate) {
      _disposed = true;
    }

    try {
      _fileStream.Dispose();
    } catch {
      // ignored
    }

    ArrayPool<byte>.Shared.Return(_formatScratch);
    ArrayPool<byte>.Shared.Return(_writeBuffer);

    if (LogData is not null && LogData.Length > 0) {
      _logger.LogInformation("Return {Channel} LogData of size: {Size}", _name, LogData.Length);
      ArrayPool.Return(LogData);
      LogData = [];
    }

    _channelBuffer.ReturnBufferToArrayPool();
  }
}
