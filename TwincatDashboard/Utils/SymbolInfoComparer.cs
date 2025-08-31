using TwincatDashboard.Models;

namespace TwincatDashboard.Utils;

public class SymbolInfoComparer : IEqualityComparer<SymbolInfo>, IComparer<SymbolInfo> {
    public bool Equals(SymbolInfo? x, SymbolInfo? y) {
        if (x == null || y == null)
            return false;

        return string.Equals(x.FullName, y.FullName, StringComparison.OrdinalIgnoreCase);
    }

    public int GetHashCode(SymbolInfo? obj) {
        if (obj == null)
            return 0;

        return obj.FullName?.ToLower().GetHashCode() ?? 0;
    }

    public int Compare(SymbolInfo? x, SymbolInfo? y) =>
        string.Compare(x!.FullName, y!.FullName, StringComparison.Ordinal);

    public static SymbolInfoComparer Instance { get; } = new SymbolInfoComparer();
}
