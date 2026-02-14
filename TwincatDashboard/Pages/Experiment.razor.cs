using Microsoft.AspNetCore.Components.Web;

using TwincatDashboard.Utils;

namespace TwincatDashboard.Pages;

public partial class Experiment {
  public required string PlcSymbolName { get; set; }
  public required string PlcSymbolType { get; set; }
  public double PlcSymbolValue { get; set; }

  private async Task ReadSymbol(MouseEventArgs arg) {
    var type = Type.GetType(PlcSymbolType);
    if (type is null) return;
    var value = await AdsComService.ReadPlcSymbolValueAsync(PlcSymbolName, type);
    if (value is null) return;
    PlcSymbolValue = SymbolExtension.ConvertObjectToDouble(value, type);
  }
}
