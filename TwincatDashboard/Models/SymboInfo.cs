using TwinCAT.TypeSystem;

namespace TwincatDashboard.Models;

public class SymbolInfo(ISymbol symbol)
{
    public ISymbol Symbol { get; set; } = symbol;
    public string Type => Symbol.DataType?.ToString() ?? "unknown";
    public string FullName => Symbol.InstancePath;
    public string Path => string.Join(".", Symbol.InstancePath.Split('.').SkipLast(1));
    public string Name => Symbol.InstancePath.Split('.').Last();
    public string ExportName => string.Join(".", Symbol.InstancePath.Split('.').Skip(1));
    #region UI Parameters

    // log->quick log->plot (dependency chain)
    private bool _isLog = false;
    private bool _isQuickLog = false;
    private bool _isPlot = false;

    public bool IsLog
    {
        get => _isLog;
        set
        {
            _isLog = value;
            if (value) return;
            _isQuickLog = false;
            _isPlot = false;
        }
    }

    public bool IsQuickLog
    {
        get => _isQuickLog;
        set
        {
            _isQuickLog = value;
            if (value)
            {
                _isLog = true;
            }
            else
            {
                _isPlot = false;
            }
        }
    }

    public bool IsPlot
    {
        get => _isPlot;
        set
        {
            _isPlot = value;
            if (value)
            {
                _isLog = true;
                _isQuickLog = true;
            }
        }
    }


    public bool IsSlowLog => !IsQuickLog;

    #endregion
}