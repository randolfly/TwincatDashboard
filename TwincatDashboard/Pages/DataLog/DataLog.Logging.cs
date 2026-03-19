using Serilog;

using System.Buffers.Binary;
using System.IO;
using System.Timers;

using TwinCAT.Ads;
using TwinCAT.Ads.TypeSystem;

using TwincatDashboard.Constants;
using TwincatDashboard.Models;
using TwincatDashboard.Services;
using TwincatDashboard.Utils;

using Timer = System.Timers.Timer;

namespace TwincatDashboard.Pages.DataLog;

public partial class DataLog {
  private readonly Timer _slowLogTimer = new();
  private readonly SemaphoreSlim _slowLogGate = new(1, 1);

  private sealed class NotificationTarget {
    public required SymbolInfo Symbol { get; init; }
    public required LogDataChannel Channel { get; init; }
    public required Func<ReadOnlySpan<byte>, double> Decoder { get; init; }
    public required bool IsPlot { get; init; }
    public int PlotCounter;
  }

  private readonly List<uint> _notificationHandles = [];
  private readonly List<string> _slowLogSymbolFullNames = [];

  private CancellationTokenSource? _loggingCts;
  private bool _isStopping;
  private int _plotDownsampleFactor = 1;

  private void InitSlowLogTimer() {
    _slowLogTimer.Interval = _logConfig.SlowLogPeriod;
    _slowLogTimer.Elapsed += OnSlowLogTimerElapsed;
  }

  private void DisposeSlowLogTimer() {
    _slowLogTimer.Elapsed -= OnSlowLogTimerElapsed;
    _slowLogTimer.Stop();
    _slowLogTimer.Dispose();
    _slowLogGate.Dispose();
  }

  private void OnSlowLogTimerElapsed(object? sender, ElapsedEventArgs e) {
    _ = SlowLogTimerElapsedAsync();
  }

  private async Task SlowLogTimerElapsedAsync() {
    if (_loggingCts?.IsCancellationRequested == true) return;

    // Prevent overlapping timer callbacks: if the previous tick is still running, skip this tick.
    if (!await _slowLogGate.WaitAsync(0)) return;

    try {
      if (_loggingCts?.IsCancellationRequested == true) return;

      var index = _availableSymbolsByFullName;
      var symbolsToRead = _slowLogSymbolFullNames.ToArray();

      foreach (var symbolName in symbolsToRead) {
        if (_loggingCts?.IsCancellationRequested == true) return;
        if (!index.TryGetValue(symbolName, out var symbol)) continue;

        try {
          var dataType = (symbol.Symbol.DataType as DataType)?.ManagedType;
          if (dataType == null) {
            Log.Warning(
                "ManagedType is null for symbol: {Symbol}, Raw Type: {RawType}",
                symbol.FullName,
                symbol.Symbol.DataType
            );
            continue;
          }

          var value = await AdsComService.ReadPlcSymbolValueAsync(symbol.FullName, dataType);
          if (value is null) {
            Log.Warning(
                "Value is null for symbol: {Symbol}, Raw Type: {RawType}",
                symbol.FullName,
                symbol.Symbol.DataType
            );
            continue;
          }

          var result = SymbolExtension.ConvertObjectToDouble(value, dataType);
          LogDataService.AddSlowLogData(symbol.FullName, result);
        } catch (Exception ex) {
          Log.Error(ex, "Slow log tick failed for symbol: {Symbol}", symbolName);
        }
      }
    } finally {
      _slowLogGate.Release();
    }
  }

  private async Task<bool> StartLogAsync() {
    if (AdsComService.GetAdsState() == AdsState.Invalid) {
      Log.Information("Ads server is not connected");
      return false;
    }

    var logSymbols = LogSymbols.ToList();
    if (logSymbols.Count == 0) {
      Log.Information("No log symbols selected");
      return false;
    }

    _isStopping = false;
    _loggingCts?.Cancel();
    _loggingCts?.Dispose();
    _loggingCts = new CancellationTokenSource();

    _slowLogTimer.Interval = _logConfig.SlowLogPeriod;

    // Keep these lists stable for the whole session (avoids enumerating / allocating from UI properties).
    var quickLogSymbols = logSymbols.Where(x => x.IsQuickLog).ToList();
    var slowLogSymbols = logSymbols.Where(x => x.IsSlowLog).ToList();
    var plotSymbols = PlotSymbols.ToList();

    try {
      await ExportLogConfigAsync(logSymbols, quickLogSymbols, plotSymbols);

      _notificationHandles.Clear();
      _slowLogSymbolFullNames.Clear();

      LogPlotService.RemoveAllChannels();
      LogDataService.RemoveAllChannels();
      LogDataService.RemoveAllSlowLog();

      // Slow log symbols: register slow log data and build the stable key list for timer reads.
      foreach (var symbol in slowLogSymbols) {
        Log.Information("Start slow log: {Symbol}, Raw Type: {RawType}", symbol.FullName, symbol.Symbol.DataType);
        LogDataService.RegisterSlowLog(symbol.FullName);
        _slowLogSymbolFullNames.Add(symbol.FullName);
      }

      // Add taskCycleCount symbol to slow log (always useful for alignment / debugging).
      var taskCycleCountSymbol = _availableSymbols.FirstOrDefault(s => s.FullName == AdsConstants.TaskCycleCountName);
      if (taskCycleCountSymbol != null) {
        LogDataService.RegisterSlowLog(taskCycleCountSymbol.FullName);
        _slowLogSymbolFullNames.Add(taskCycleCountSymbol.FullName);
      }

      _slowLogTimer.Start();

      // Quick log symbols
      var quickLogPeriod = Math.Max(1, _logConfig.QuickLogPeriod);
      _plotDownsampleFactor = ComputePlotDownsampleFactor(quickLogPeriod);
      foreach (var symbol in quickLogSymbols) {
        Log.Information("Start quick log: {Symbol}, Raw Type: {RawType}", symbol.FullName, symbol.Symbol.DataType);

        var channel = LogDataService.GetOrAddChannel(symbol.FullName);
        if (channel is null)
          continue;

        var managedType = (symbol.Symbol.DataType as DataType)?.ManagedType;
        if (managedType is null) {
          Log.Warning("ManagedType is null for symbol: {Symbol}, Raw Type: {RawType}", symbol.FullName, symbol.Symbol.DataType);
          continue;
        }

        if (!TryCreateDecoder(managedType, out var decoder)) {
          Log.Warning("Unsupported quick log symbol type: {Symbol} ManagedType={ManagedType} RawType={RawType}", symbol.FullName, managedType, symbol.Symbol.DataType);
          continue;
        }

        var target = new NotificationTarget {
          Symbol = symbol,
          Channel = channel,
          Decoder = decoder,
          IsPlot = symbol.IsPlot
        };

        var notificationHandle = AdsComService.AddDeviceNotificationEx(
            symbol.Symbol.InstancePath,
            new NotificationSettings(AdsTransMode.Cyclic, quickLogPeriod, 0),
            userData: target,
            type: managedType
        );

        if (notificationHandle == 0) {
          Log.Warning("Failed to register ADS notification for symbol: {Symbol}", symbol.FullName);
          continue;
        }

        _notificationHandles.Add(notificationHandle);
      }

      // Plot symbols
      foreach (var symbol in plotSymbols) {
        var capacity = (int)Math.Floor(3000.0 / quickLogPeriod);
        if (capacity < 1) capacity = 1;
        LogPlotService.AddChannel(symbol.FullName, capacity);
      }

      AdsComService.AddNotificationExHandler(AdsNotificationExHandler);
      return true;
    } catch (Exception ex) {
      Log.Error(ex, "Failed to start logging session.");
      await StopLogInfrastructureAsync();
      return false;
    }
  }

  private async Task StopLogAsync() {
    await StopLogInternalAsync(exportData: true);
  }

  private async Task StopLogInternalAsync(bool exportData) {
    if (_isStopping) return;
    _isStopping = true;

    await StopLogInfrastructureAsync();

    if (!exportData)
      return;

    // quick log data: load data from tmp folder, actual length is LogDataService.QuickLogDict[channel].DataLength
    await LogDataService.FlushAllChannelsAsync();
    var quickLogResult = await LogDataService.LoadAllChannelsAsync();

    var slowLogResult = new Dictionary<string, double[]>();
    foreach (var slowLog in LogDataService.SlowLogDict.ToArray())
      slowLogResult.Add(slowLog.Key, slowLog.Value.ToArray());

    // export
    if (!Directory.Exists(_logConfig.FolderName))
      Directory.CreateDirectory(_logConfig.FolderName);

    var exportQuickDataLength = LogDataService.QuickLogDict.Count > 0
        ? LogDataService.QuickLogDict.Values.Min(channel => channel.DataLength)
        : 0;

    var exportSlowDataLength = LogDataService.SlowLogDict.Count > 0
        ? LogDataService.SlowLogDict.Values.Min(list => list.Count)
        : 0;

    if (quickLogResult.Count > 0 && exportQuickDataLength > 0) {
      await LogDataService.ExportDataAsync(
          quickLogResult,
          _logConfig.QuickLogFileFullName,
          _logConfig.FileType,
          exportQuickDataLength
      );
    }

    if (slowLogResult.Count > 0 && exportSlowDataLength > 0) {
      await LogDataService.ExportDataAsync(
          slowLogResult,
          _logConfig.SlowLogFileFullName,
          _logConfig.FileType,
          exportSlowDataLength
      );
    }

    // plot
    if (quickLogResult.Count > 0 && exportQuickDataLength > 0 && PlotSymbols.Any()) {
      LogPlotService.ShowAllChannelsWithNewData(quickLogResult, exportQuickDataLength, _logConfig.QuickLogPeriod);
    }

    LogDataService.RemoveAllSlowLog();

    // delete tmp files and release buffers
    LogDataService.DeleteTmpFiles();
    LogDataService.RemoveAllChannels();
  }

  private async Task StopLogInfrastructureAsync() {
    // Cancel first so slow/quick handlers can short-circuit quickly.
    _loggingCts?.Cancel();

    AdsComService.RemoveNotificationExHandler(AdsNotificationExHandler);

    foreach (var handle in _notificationHandles.ToArray())
      AdsComService.RemoveDeviceNotification(handle);

    _slowLogTimer.Stop();

    // Ensure no slow log callback is in-flight while we snapshot/export/clear dictionaries.
    await _slowLogGate.WaitAsync();
    try {
      _notificationHandles.Clear();
      _slowLogSymbolFullNames.Clear();
    } finally {
      _slowLogGate.Release();
    }

    _loggingCts?.Dispose();
    _loggingCts = null;
  }

  private async Task ExportLogConfigAsync(
      List<SymbolInfo> logSymbols,
      List<SymbolInfo> quickLogSymbols,
      List<SymbolInfo> plotSymbols
  ) {
    _logConfig.LogSymbols = logSymbols.Select(s => s.FullName).ToList();
    _logConfig.QuickLogSymbols = quickLogSymbols.Select(s => s.FullName).ToList();
    _logConfig.PlotSymbols = plotSymbols.Select(s => s.FullName).ToList();

    await ConfigStore.SaveAsync();
  }

  private void AdsNotificationExHandler(object? sender, AdsNotificationExEventArgs e) {
    try {
      if (_isStopping || _loggingCts?.IsCancellationRequested == true)
        return;

      if (e.UserData is not NotificationTarget target) {
        return;
      }

      var symbol = target.Symbol;
      var result = target.Decoder(e.Data.Span);

      target.Channel.Add(result);

      if (target.IsPlot && ShouldForwardPlotSample(target)) {
        LogPlotService.AddData(symbol.FullName, result);
      }
    } catch (Exception ex) {
      Log.Error(ex, "Error in AdsNotificationExHandler");
    }
  }

  private bool ShouldForwardPlotSample(NotificationTarget target) {
    if (_plotDownsampleFactor <= 1)
      return true;

    // Per-handle counter: avoids per-sample dictionary lookups.
    var next = target.PlotCounter + 1;
    if (next >= _plotDownsampleFactor) {
      target.PlotCounter = 0;
      return true;
    }

    target.PlotCounter = next;
    return false;
  }

  private static int ComputePlotDownsampleFactor(int quickLogPeriodMs) {
    // Plot refresh is ~20 FPS (50ms). There is little value feeding 1000 Hz samples into UI.
    const int targetPlotHz = 100;
    var period = Math.Max(1, quickLogPeriodMs);
    var sampleRateHz = 1000 / period;
    var factor = sampleRateHz / targetPlotHz;
    return Math.Max(1, factor);
  }

  private static Func<ReadOnlySpan<byte>, double> CreateDecoder(Type managedType) {
    if (!TryCreateDecoder(managedType, out var decoder))
      throw new NotSupportedException($"Managed type '{managedType}' is not a supported primitive scalar type.");

    return decoder;
  }

  private static bool TryCreateDecoder(Type managedType, out Func<ReadOnlySpan<byte>, double> decoder) {
    // Important: this runs once per registered symbol, not per sample.
    // Keep supported types explicit to guarantee no boxing/reflection in the 1ms hot path.
    if (managedType == typeof(bool)) {
      decoder = static span => span.Length >= 1 && span[0] != 0 ? 1d : 0d;
      return true;
    }

    if (managedType == typeof(byte)) {
      decoder = static span => span.Length >= 1 ? span[0] : 0d;
      return true;
    }

    if (managedType == typeof(sbyte)) {
      decoder = static span => span.Length >= 1 ? (sbyte)span[0] : 0d;
      return true;
    }

    if (managedType == typeof(short)) {
      decoder = static span => span.Length >= 2 ? BinaryPrimitives.ReadInt16LittleEndian(span) : 0d;
      return true;
    }

    if (managedType == typeof(ushort)) {
      decoder = static span => span.Length >= 2 ? BinaryPrimitives.ReadUInt16LittleEndian(span) : 0d;
      return true;
    }

    if (managedType == typeof(int)) {
      decoder = static span => span.Length >= 4 ? BinaryPrimitives.ReadInt32LittleEndian(span) : 0d;
      return true;
    }

    if (managedType == typeof(uint)) {
      decoder = static span => span.Length >= 4 ? BinaryPrimitives.ReadUInt32LittleEndian(span) : 0d;
      return true;
    }

    if (managedType == typeof(long)) {
      decoder = static span => span.Length >= 8 ? BinaryPrimitives.ReadInt64LittleEndian(span) : 0d;
      return true;
    }

    if (managedType == typeof(ulong)) {
      decoder = static span => span.Length >= 8 ? BinaryPrimitives.ReadUInt64LittleEndian(span) : 0d;
      return true;
    }

    if (managedType == typeof(float)) {
      decoder = static span => span.Length >= 4
          ? BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(span))
          : 0d;
      return true;
    }

    if (managedType == typeof(double)) {
      decoder = static span => span.Length >= 8
          ? BitConverter.Int64BitsToDouble(BinaryPrimitives.ReadInt64LittleEndian(span))
          : 0d;
      return true;
    }

    decoder = static _ => 0d;
    return false;
  }
}
