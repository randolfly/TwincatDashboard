using TwinCAT.TypeSystem;

using TwincatDashboard.Models;

namespace TwincatDashboard.Utils;

public static class SymbolExtension {
    public static string GetSymbolName(this ISymbol symbol) {
        return symbol.InstancePath.ToLowerInvariant();
    }

    public static SymbolInfo DeepCopy(this SymbolInfo symbol) {
        return new SymbolInfo(symbol.Symbol);
    }

    public static double ConvertObjectToDouble(object data, Type dataType) {
        var result = data switch {
            bool b => b ? 1.0 : 0.0,
            byte b => b,
            sbyte sb => sb,
            short s => s,
            ushort us => us,
            int i => i,
            uint ui => ui,
            long l => l,
            ulong ul => ul,
            float f => f,
            double d => d,
            _ => throw new InvalidCastException($"Unsupported data type: {dataType}"),
        };
        return result;
    }
}
