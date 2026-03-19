using FuzzySharp;

using Masa.Blazor;

using Microsoft.AspNetCore.Components.Web;

using Serilog;

using System.Diagnostics;

using TwincatDashboard.Models;

namespace TwincatDashboard.Pages.DataLog;

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

  // Allocation-free views for UI. Materialize via .ToList() only when needed.
  private IEnumerable<SymbolInfo> LogSymbols => _availableSymbols.Where(x => x.IsLog);
  private IEnumerable<SymbolInfo> PlotSymbols => _availableSymbols.Where(x => x.IsPlot);

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
    _logConfig = ConfigStore.Current.LogConfig;
    InitSlowLogTimer();
  }

  private async Task SetLoggingStateAsync(bool shouldStart) {
    if (_isLoggingBusy) return;
    if (shouldStart == _startLogging) return;

    _startLogging = shouldStart; // optimistic UI; corrected if start fails
    _isLoggingBusy = true;
    await InvokeAsync(StateHasChanged);

    try {
      if (shouldStart) {
        _startLogging = await StartLogAsync();
      } else {
        await StopLogAsync();
      }
    } finally {
      _isLoggingBusy = false;
      await InvokeAsync(StateHasChanged);
    }
  }

  public static IEnumerable<SymbolInfo> FilterSymbolBySimilarityScore(
      IEnumerable<SymbolInfo> items,
      IEnumerable<ItemValue<SymbolInfo>> _,
      string? search
  ) {
    if (string.IsNullOrEmpty(search)) return items;
    return items.OrderByDescending(s => GetSimilarityScore(search, s));
  }

  private static int GetSimilarityScore(string searchText, SymbolInfo symbolInfo) {
    return Fuzz.PartialTokenSetRatio(searchText, symbolInfo.FullName.ToLowerInvariant());
  }

  private void CloseAllPlotWindows(MouseEventArgs _) {
    if (EditingDisabled) return;
    LogPlotService.RemoveAllChannels();
  }

  // Just used to trigger the MDataTable search action (enter key in filter field).
  private void OnSearchLogSymbolNameChanged(InputsFilterFieldChangedEventArgs _) {
    _searchLogSymbolName = _searchLogSymbolTmpName;
  }

  private void OnSearchAvailableSymbolNameChanged(InputsFilterFieldChangedEventArgs _) {
    _searchAvailableSymbolName = _searchAvailableSymbolTmpName;
  }

  private void OpenConfigFolder(MouseEventArgs _) {
    System.Diagnostics.Process.Start(new ProcessStartInfo {
      FileName = Paths.AppDataDirectory,
      UseShellExecute = true
    });
  }

  public async ValueTask DisposeAsync() {
    try {
      if (_startLogging) {
        _startLogging = false;
        await StopLogAsync();
      }

      DisposeSlowLogTimer();
    } catch (Exception ex) {
      Log.Error(ex, "Error while disposing DataLog component");
    }
  }
}
