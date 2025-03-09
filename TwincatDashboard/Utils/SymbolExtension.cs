using TwinCAT.TypeSystem;
using TwincatDashboard.Models;

namespace TwincatDashboard.Utils;
public static class SymbolExtension
{
    public static string GetSymbolName(this ISymbol symbol) {
        return symbol.InstancePath.ToLowerInvariant();
    }
    
    public static SymbolInfo DeepCopy(this SymbolInfo symbol)
    {
        return new SymbolInfo(symbol.Symbol);
    }
}