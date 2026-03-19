using Serilog;

using TwinCAT.Ads;

using TwincatDashboard.Constants;
using TwincatDashboard.Models;

namespace TwincatDashboard.Pages.DataLog;

public partial class DataLog {
  private bool _isFirstGetAvailableSymbols = true;

  // Rebuilt by swapping references so the slow-log timer can safely read it without locks.
  private IReadOnlyDictionary<string, SymbolInfo> _availableSymbolsByFullName =
      new Dictionary<string, SymbolInfo>(StringComparer.Ordinal);

  private void GetAvailableSymbols() {
    if (AdsComService.GetAdsState() == AdsState.Invalid) {
      Log.Information("Ads server is not connected");
      return;
    }

    if (!_isFirstGetAvailableSymbols && _availableSymbols.Count > 0) {
      var tmpAvailableSymbols = AdsComService.GetAvailableSymbols(_logConfig.ReadNamespace);

      var existing = new HashSet<string>(_availableSymbols.Select(s => s.FullName), StringComparer.Ordinal);
      foreach (var symbol in tmpAvailableSymbols) {
        if (existing.Add(symbol.FullName))
          _availableSymbols.Add(symbol);
      }
    } else {
      _availableSymbols = AdsComService.GetAvailableSymbols(_logConfig.ReadNamespace);
    }

    _availableSymbols.Sort((a, b) => string.Compare(a.FullName, b.FullName, StringComparison.Ordinal));
    RebuildAvailableSymbolsIndex();

    Log.Information("Available symbols: {Count}", _availableSymbols.Count);

    // Add default quick log symbol: AdsConstants.TaskCycleCountName
    var taskCycleCountSymbol = _availableSymbols.FirstOrDefault(s => s.FullName == AdsConstants.TaskCycleCountName);
    if (taskCycleCountSymbol != null)
      taskCycleCountSymbol.IsQuickLog = true;

    // Use hash sets to avoid O(n^2) .Contains calls on potentially large symbol lists.
    var logSet = new HashSet<string>(_logConfig.LogSymbols, StringComparer.Ordinal);
    var quickSet = new HashSet<string>(_logConfig.QuickLogSymbols, StringComparer.Ordinal);
    var plotSet = new HashSet<string>(_logConfig.PlotSymbols, StringComparer.Ordinal);

    foreach (var symbol in _availableSymbols) {
      if (logSet.Contains(symbol.FullName)) {
        symbol.IsLog = true;
        if (quickSet.Contains(symbol.FullName))
          symbol.IsQuickLog = true;
      }

      if (plotSet.Contains(symbol.FullName))
        symbol.IsPlot = true;
    }

    _ = TryLoadQuickLogPeriodAsync();
    _isFirstGetAvailableSymbols = false;
  }

  private void RebuildAvailableSymbolsIndex() {
    var next = new Dictionary<string, SymbolInfo>(StringComparer.Ordinal);
    foreach (var symbol in _availableSymbols)
      next[symbol.FullName] = symbol;

    _availableSymbolsByFullName = next;
  }

  private async Task TryLoadQuickLogPeriodAsync() {
    try {
      _logConfig.QuickLogPeriod = await AdsComService.GetTaskCycleTimeAsync();
      await InvokeAsync(StateHasChanged);
    } catch (Exception ex) {
      Log.Error(ex, "Failed to load PLC task cycle time (QuickLogPeriod).");
    }
  }
}

