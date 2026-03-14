using FuzzySharp;

using Masa.Blazor;

using Microsoft.AspNetCore.Components.Web;

using Serilog;

using System.Diagnostics;
using System.IO;
using System.Runtime;
using System.Threading;
using System.Timers;

using TwinCAT.Ads;
using TwinCAT.Ads.TypeSystem;

using TwincatDashboard.Constants;
using TwincatDashboard.Models;
using TwincatDashboard.Services;
using TwincatDashboard.Utils;

using Process = System.Diagnostics.Process;
using Timer = System.Timers.Timer;

namespace TwincatDashboard.Pages;

public partial class DataLog : IAsyncDisposable {
  private LogConfig _logConfig = new();

  private bool _startLogging;
  private bool _isLoggingBusy;

  public bool StartLogging {
    get => _startLogging;
    set => _ = SetLoggingStateAsync(value);
  }

  public bool IsLoggingBusy => _isLoggingBusy;

  public bool EditingDisabled => _startLogging || _isLoggingBusy;

  private string? _searchAvailableSymbolName;
  private string? _searchAvailableSymbolTmpName;
  private string? _searchLogSymbolName;
  private string? _searchLogSymbolTmpName;

  private List<SymbolInfo> _availableSymbols = [];
  private List<SymbolInfo> LogSymbols => _availableSymbols.Where(x => x.IsLog).ToList();
  private List<SymbolInfo> PlotSymbols => _availableSymbols.Where(x => x.IsPlot).ToList();
  private List<SymbolInfo> QuickLogSymbols => LogSymbols.Where(x => x.IsQuickLog).ToList();
  private List<SymbolInfo> SlowLogSymbols => LogSymbols.Where(x => x.IsSlowLog).ToList();

  private readonly Timer _slowLogTimer = new();
  private readonly SemaphoreSlim _slowLogGate = new(1, 1);

  private readonly Dictionary<uint, SymbolInfo> _symbolsDict = [];
  private readonly Dictionary<string, SymbolInfo> _availableSymbolsByFullName = new(StringComparer.Ordinal);
  private bool _isFirstGetAvailableSymbols = true;

  #region UI Params

  private readonly List<DataTableHeader<SymbolInfo>> _availableSymbolHeaders =
  [
    new() { Text = "Log", Value = nameof(SymbolInfo.IsLog), Filterable = false },
    new() { Text = "FullName", Value = nameof(SymbolInfo.Name), Filterable = false },
    new() { Text = "Path", Value = nameof(SymbolInfo.Path), Filterable = false },
    new() { Text = "Type", Value = nameof(SymbolInfo.Type), Filterable = false }
  ];

  private readonly List<DataTableHeader<SymbolInfo>> _logSymbolHeaders =
  [
    new() { Text = "Log", Value = nameof(SymbolInfo.IsLog), Filterable = false },
    new() { Text = "QuickLog", Value = nameof(SymbolInfo.IsQuickLog), Filterable = false },
    new() { Text = "Plot", Value = nameof(SymbolInfo.IsPlot), Filterable = false },
    new() { Text = "FullName", Value = nameof(SymbolInfo.Name), Filterable = false },
    new() { Text = "Path", Value = nameof(SymbolInfo.Path), Filterable = false },
    new() { Text = "Type", Value = nameof(SymbolInfo.Type), Filterable = false }
  ];

  #endregion

  protected override void OnInitialized() {
    _logConfig = AppConfigService.AppConfig.LogConfig;
    _slowLogTimer.Interval = _logConfig.SlowLogPeriod;
    _slowLogTimer.Elapsed += OnSlowLogTimerElapsed;
  }

  private void OnSlowLogTimerElapsed(object? sender, ElapsedEventArgs e) {
    _ = SlowLogTimerElapsedAsync();
  }

  private async Task SetLoggingStateAsync(bool shouldStart) {
    if (_isLoggingBusy) return;
    if (shouldStart == _startLogging) return;

    _startLogging = shouldStart; // optimistic UI; corrected if start fails
    _isLoggingBusy = true;
    await InvokeAsync(StateHasChanged);

    try {
      if (shouldStart) {
        _startLogging = StartLog();
      } else {
        await StopLogAsync();
      }
    } finally {
      _isLoggingBusy = false;
      await InvokeAsync(StateHasChanged);
    }
  }

  private async Task SlowLogTimerElapsedAsync() {
    // Prevent overlapping timer callbacks: if the previous tick is still running, skip this tick.
    if (!await _slowLogGate.WaitAsync(0)) return;

    try {
      var slowLogKeys = LogDataService.SlowLogDict.Keys.ToArray();
      foreach (var symbolName in slowLogKeys) {
        if (!_availableSymbolsByFullName.TryGetValue(symbolName, out var symbol)) continue;

        try {
          var dataType = (symbol.Symbol.DataType as DataType)?.ManagedType;
          if (dataType == null) {
            Log.Warning("ManagedType is null for symbol: {Symbol}, Raw Type: {RawType}", symbol.FullName, symbol.Symbol.DataType);
            continue;
          }

          var value = await AdsComService.ReadPlcSymbolValueAsync(symbol.FullName, dataType);
          if (value is null) {
            Log.Warning("Value is null for symbol: {Symbol}, Raw Type: {RawType}", symbol.FullName, symbol.Symbol.DataType);
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

  private void GetAvailableSymbols() {
    if (AdsComService.GetAdsState() == AdsState.Invalid) {
      Log.Information("Ads server is not connected");
      return;
    }

    if (!_isFirstGetAvailableSymbols && _availableSymbols.Any()) {
      var tmpAvailableSymbols = AdsComService.GetAvailableSymbols(_logConfig.ReadNamespace);
      var changeSymbols = tmpAvailableSymbols
        .Where(symbol => _availableSymbols.All(s => s.FullName != symbol.FullName));
      foreach (var symbol in changeSymbols) {
        _availableSymbols.Add(symbol);
      }
    } else {
      _availableSymbols = AdsComService.GetAvailableSymbols(_logConfig.ReadNamespace);
    }

    _availableSymbols.Sort((a, b) => string.Compare(a.FullName, b.FullName, StringComparison.Ordinal));
    RebuildAvailableSymbolsIndex();
    Log.Information("Available symbols: {0}", _availableSymbols.Count);

    // add default quick log symbol: AdsConstants.TaskCycleCountName
    var taskCycleCountSymbol = _availableSymbols.FirstOrDefault(s => s.FullName == AdsConstants.TaskCycleCountName);
    if (taskCycleCountSymbol != null) taskCycleCountSymbol.IsQuickLog = true;

    // Set default log symbols by checking the config
    foreach (var symbol in _availableSymbols) {
      if (_logConfig.LogSymbols.Contains(symbol.FullName)) {
        symbol.IsLog = true;
        if (_logConfig.QuickLogSymbols.Contains(symbol.FullName)) {
          symbol.IsQuickLog = true;
        }
      }

      if (_logConfig.PlotSymbols.Contains(symbol.FullName)) {
        symbol.IsPlot = true;
      }
    }

    _ = Task.Run(async () => _logConfig.QuickLogPeriod = await AdsComService.GetTaskCycleTimeAsync());
    _isFirstGetAvailableSymbols = false;
  }

  private void RebuildAvailableSymbolsIndex() {
    _availableSymbolsByFullName.Clear();
    foreach (var symbol in _availableSymbols) {
      _availableSymbolsByFullName[symbol.FullName] = symbol;
    }
  }

  /// <summary>
  ///   start logging
  /// </summary>
  /// <returns>true->start log successfully, false->error</returns>
  private bool StartLog() {
    if (AdsComService.GetAdsState() == AdsState.Invalid) {
      Log.Information("Ads server is not connected");
      return false;
    }

    _slowLogTimer.Interval = _logConfig.SlowLogPeriod;
    if (LogSymbols.Count == 0) {
      Log.Information("No log symbols selected");
      return false;
    }

    ExportLogConfig();

    _symbolsDict.Clear();
    LogPlotService.RemoveAllChannels();
    LogDataService.RemoveAllChannels();
    // slow log symbols: register slow log data
    foreach (var symbol in SlowLogSymbols) {
      Log.Information("Start slow log: {0}, Raw Type: {1}", symbol.FullName, symbol.Symbol.DataType);
      LogDataService.RegisterSlowLog(symbol.FullName);
    }

    // add taskCycleCount symbol to slow log
    var taskCycleCountSymbol = _availableSymbols
      .FirstOrDefault(s => s.FullName == AdsConstants.TaskCycleCountName);
    if (taskCycleCountSymbol != null) LogDataService.RegisterSlowLog(taskCycleCountSymbol.FullName);

    _slowLogTimer.Start();
    // quick log symbols
    foreach (var symbol in QuickLogSymbols) {
      Log.Information("Start quick log: {0}, Raw Type: {1}", symbol.FullName, symbol.Symbol.DataType);
      var notificationHandle = AdsComService.AddDeviceNotification(
        symbol.Symbol.InstancePath,
        symbol.Symbol.ByteSize,
        new NotificationSettings(AdsTransMode.Cyclic,
          AppConfigService.AppConfig.LogConfig.QuickLogPeriod, 0));
      _symbolsDict.Add(notificationHandle, symbol);
      LogDataService.AddChannel(symbol.FullName);
    }

    // plot symbols
    foreach (var symbol in PlotSymbols) {
      LogPlotService.AddChannel(symbol.FullName,
        (int)Math.Floor(3000.0 / _logConfig.QuickLogPeriod));
    }

    AdsComService.AddNotificationHandler(AdsNotificationHandler);
    return true;
  }

  /// <summary>
  ///   stop logging
  /// </summary>
  /// <returns>true->stop log successfully, false->error</returns>
  private async Task<bool> StopLogAsync() {
    AdsComService.RemoveNotificationHandler(AdsNotificationHandler);
    _symbolsDict.Keys.ToList().ForEach(handle =>
      AdsComService.RemoveDeviceNotification(handle));
    _slowLogTimer.Stop();

    _symbolsDict.Clear();

    // quick log data: load data from tmp folder, the actual length of log data is LogDataService.QuickLogDict[channel].DataLength
    var quickLogResult = await LogDataService.LoadAllChannelsAsync();
    var slowLogResult = new Dictionary<string, double[]>();
    foreach (var slowLog in LogDataService.SlowLogDict.ToArray()) {
      slowLogResult.Add(slowLog.Key, slowLog.Value.ToArray());
    }

    // export
    // check if data folder exists, if not, create it
    if (!Directory.Exists(_logConfig.FolderName)) {
      Directory.CreateDirectory(_logConfig.FolderName);
    }

    var exportQuickDataLength = LogDataService.QuickLogDict.Count > 0
      ? LogDataService.QuickLogDict.Values.Min(channel => channel.DataLength)
      : 0;
    var exportSlowDataLength = LogDataService.SlowLogDict.Count > 0
      ? LogDataService.SlowLogDict.Values.Min(list => list.Count)
      : 0;

    if (quickLogResult.Count > 0 && exportQuickDataLength > 0) {
      await LogDataService.ExportDataAsync(quickLogResult,
        _logConfig.QuickLogFileFullName, _logConfig.FileType, exportQuickDataLength);
    }

    if (slowLogResult.Count > 0 && exportSlowDataLength > 0) {
      await LogDataService.ExportDataAsync(slowLogResult,
        _logConfig.SlowLogFileFullName, _logConfig.FileType, exportSlowDataLength);
    }

    // plot
    if (quickLogResult.Count > 0 && exportQuickDataLength > 0 && PlotSymbols.Count > 0) {
      LogPlotService.ShowAllChannelsWithNewData(quickLogResult, exportQuickDataLength, _logConfig.QuickLogPeriod);
    }
    LogDataService.RemoveAllSlowLog();

    // delete tmp files
    LogDataService.DeleteTmpFiles();

    return true;
  }

  private void ExportLogConfig() {
    _logConfig.LogSymbols = LogSymbols.Select(s => s.FullName).ToList();
    _logConfig.QuickLogSymbols = QuickLogSymbols.Select(s => s.FullName).ToList();
    _logConfig.PlotSymbols = PlotSymbols.Select(s => s.FullName).ToList();
    AppConfigService.SaveConfig(AppConfig.ConfigFileFullName);
  }

  private void AdsNotificationHandler(object? sender, AdsNotificationEventArgs e) {
    _ = HandleAdsNotificationAsync(e);
  }

  private async Task HandleAdsNotificationAsync(AdsNotificationEventArgs e) {
    try {
      if (!_symbolsDict.TryGetValue(e.Handle, out var symbol)) {
        Log.Warning("Symbol not found for handle: {0}", e.Handle);
        return;
      }

      var dataType = (symbol.Symbol.DataType as DataType)?.ManagedType;
      if (dataType == null) {
        Log.Warning("ManagedType is null for symbol: {0}", symbol.FullName);
        return;
      }

      object data;
      try {
        data = SpanConverter.ConvertTo(e.Data.Span, dataType);
      } catch (Exception ex) {
        Log.Error("Error converting data for symbol: {0}, Exception: {1}", symbol.FullName, ex);
        return;
      }

      var result = SymbolExtension.ConvertObjectToDouble(data, dataType);

      await LogDataService.AddDataAsync(symbol.FullName, result);
      LogPlotService.AddData(symbol.FullName, result);
    } catch (Exception exception) {
      Log.Error(exception, "Error in AdsNotificationHandler");
    }
  }

  public static IEnumerable<SymbolInfo> FilterSymbolBySimilarityScore(IEnumerable<SymbolInfo> items, IEnumerable<ItemValue<SymbolInfo>> itemValues, string? search) {
    if (string.IsNullOrEmpty(search)) return items;
    var searchResults = items
      .OrderByDescending(s => GetSimilarityScore(search, s));
    return searchResults;
  }

  private static int GetSimilarityScore(string searchText, SymbolInfo symbolInfo) {
    return Fuzz.PartialTokenSetRatio(searchText, symbolInfo.FullName.ToLowerInvariant());
  }

  private void CloseAllPlotWindows(MouseEventArgs obj) {
    if (EditingDisabled) return;
    LogPlotService.RemoveAllChannels();
  }

  /// <summary>
  ///   Just used for trigger MDataTable Search Action
  /// </summary>
  /// <param name="obj"></param>
  private void OnSearchLogSymbolNameChanged(InputsFilterFieldChangedEventArgs obj) {
    // StateHasChanged();
    _searchLogSymbolName = _searchLogSymbolTmpName;
  }

  private void OnSearchAvailableSymbolNameChanged(InputsFilterFieldChangedEventArgs obj) {
    _searchAvailableSymbolName = _searchAvailableSymbolTmpName;
  }

  private void OpenConfigFolder(MouseEventArgs obj) {
    Process.Start(new ProcessStartInfo {
      FileName = AppConfig.FolderName,
      UseShellExecute = true
    });
  }

  public async ValueTask DisposeAsync() {
    try {
      _slowLogTimer.Elapsed -= OnSlowLogTimerElapsed;
      if (_startLogging) {
        _startLogging = false;
        await StopLogAsync();
      }

      _slowLogTimer.Stop();
      _slowLogTimer.Dispose();
    } catch (Exception ex) {
      Log.Error(ex, "Error while disposing DataLog component");
    } finally {
      _slowLogGate.Dispose();
    }
  }

}
