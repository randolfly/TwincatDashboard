using FuzzySharp;

using Masa.Blazor;

using Microsoft.AspNetCore.Components.Web;

using Serilog;

using System.Diagnostics;
using System.IO;
using System.Runtime;

using TwinCAT.Ads;
using TwinCAT.Ads.TypeSystem;

using TwincatDashboard.Constants;
using TwincatDashboard.Models;
using TwincatDashboard.Services;
using TwincatDashboard.Utils;

using Process = System.Diagnostics.Process;
using Timer = System.Timers.Timer;

namespace TwincatDashboard.Pages;

public partial class DataLog {
  private LogConfig _logConfig = new();

  private bool _startLogging;

  public bool StartLogging {
    get => _startLogging;
    set {
      if (value) {
        _startLogging = StartLog();
      } else {
        _startLogging = !Task.Run(StopLogAsync).Result;
      }
    }
  }

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

  private readonly Dictionary<uint, SymbolInfo> _symbolsDict = [];
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
    _slowLogTimer.Elapsed += async (sender, e) => await SlowLogTimerElapsedAsync();
  }

  private async Task SlowLogTimerElapsedAsync() {
    foreach (var symbolName in LogDataService.SlowLogDict.Keys) {
      var symbol = _availableSymbols.FirstOrDefault(s => s.FullName == symbolName)!;
      var dataType = (symbol.Symbol.DataType as DataType)?.ManagedType;
      if (dataType == null) {
        Log.Warning("ManagedType is null for symbol: {0}, Raw Type: {1}", symbol.FullName, symbol.Symbol.DataType);
        return;
      }

      var value = await AdsComService.ReadPlcSymbolValueAsync(symbol.FullName, dataType);
      if (value is null) {
        Log.Warning("Value is null for symbol: {0}, Raw Type: {1}", symbol.FullName, symbol.Symbol.DataType);
        return;
      }

      var result = SymbolExtension.ConvertObjectToDouble(value, dataType);
      LogDataService.AddSlowLogData(symbol.FullName, result);
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
    Log.Information("Available symbols: {0}", _availableSymbols.Count);

    // add default quick log symbol: AdsConstants.TaskCycleCountName
    var taskCycleCountSymbol = _availableSymbols.FirstOrDefault(s => s.FullName == AdsConstants.TaskCycleCountName);
    if (taskCycleCountSymbol != null) taskCycleCountSymbol.IsQuickLog = true;

    // Set default log symbols by checking the config
    _availableSymbols.AsParallel().ForAll(symbol => {
      if (_logConfig.LogSymbols.Contains(symbol.FullName)) {
        symbol.IsLog = true;
        if (_logConfig.QuickLogSymbols.Contains(symbol.FullName)) {
          symbol.IsQuickLog = true;
        }
      }

      if (_logConfig.PlotSymbols.Contains(symbol.FullName)) {
        symbol.IsPlot = true;
      }
    });
    Task.Run(async () => _logConfig.QuickLogPeriod = await AdsComService.GetTaskCycleTimeAsync());
    _isFirstGetAvailableSymbols = false;
  }

  /// <summary>
  ///   start logging
  /// </summary>
  /// <returns>true->start log successfully, false->error</returns>
  private bool StartLog() {
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
    foreach (var slowLog in LogDataService.SlowLogDict) {
      slowLogResult.Add(slowLog.Key, slowLog.Value.ToArray());
    }

    // export
    // check if data folder exists, if not, create it
    if (!Directory.Exists(_logConfig.FolderName)) {
      Directory.CreateDirectory(_logConfig.FolderName);
    }

    var exportQuickDataLength = LogDataService.QuickLogDict.Values.Min(channel => channel.DataLength);
    var exportSlowDataLength = LogDataService.SlowLogDict.Values.Min(list => list.Count);
    await LogDataService.ExportDataAsync(quickLogResult,
      _logConfig.QuickLogFileFullName, _logConfig.FileType, exportQuickDataLength);
    await LogDataService.ExportDataAsync(slowLogResult,
      _logConfig.SlowLogFileFullName, _logConfig.FileType, exportSlowDataLength);

    // plot
    LogPlotService.ShowAllChannelsWithNewData(quickLogResult, exportQuickDataLength, _logConfig.QuickLogPeriod);
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

  private async void AdsNotificationHandler(object? sender, AdsNotificationEventArgs e) {
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
    return Fuzz.PartialTokenSetRatio(searchText, symbolInfo.FullName.ToLower());
  }

  private void CloseAllPlotWindows(MouseEventArgs obj) {
    if (StartLogging) return;
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

}
