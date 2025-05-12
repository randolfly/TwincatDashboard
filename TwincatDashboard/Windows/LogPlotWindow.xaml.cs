using System.Windows;
using System.Windows.Input;
using ScottPlot;
using ScottPlot.Plottables;
using Timer = System.Timers.Timer;

namespace TwincatDashboard.Windows;

public partial class LogPlotWindow : Window, IDisposable
{
    private string LogName { get; set; }
    private readonly DataStreamer _dataStreamer;
    private readonly Timer _updatePlotTimer = new() { Interval = 50, Enabled = true, AutoReset = true };

    private SignalXY? _fullDataSignal;
    private Crosshair? _fullDataCrosshair;

    public LogPlotWindow(string title, int logNum)
    {
        InitializeComponent();

        LogName = title;
        Title = LogName;
        // will cause const data flow not render!
        //LogPlot.Plot.Axes.ContinuouslyAutoscale = true;
        LogPlot.Plot.ScaleFactor = 1.0;

        _dataStreamer = LogPlot.Plot.Add.DataStreamer(logNum);
        _dataStreamer.ViewScrollLeft();

        // setup a timer to request a render periodically
        _updatePlotTimer.Elapsed += (s, e) =>
        {
            if (_dataStreamer.HasNewData)
            {
                LogPlot.Refresh();
            }

            LogPlot.Plot.Axes.AutoScale();
        };
    }

    public void UpdatePlot(double newData)
    {
        // note: could be optimized by adding multiple points at once
        _dataStreamer.Add(newData);
        // slide marker to the left
        LogPlot.Plot.GetPlottables<Marker>()
            .ToList()
            .ForEach(m => m.X -= 1);

        // remove off-screen marks
        LogPlot.Plot.GetPlottables<Marker>()
            .Where(m => m.X < 0)
            .ToList()
            .ForEach(m => LogPlot.Plot.Remove(m));
    }

    /// <summary>
    /// manage plot window position by plot id
    /// </summary>
    /// <param name="windowId">the id of plot window in the plot dict</param>
    public void SetPlotViewWindowPosById(int windowId)
    {
        var screenWidth = SystemParameters.PrimaryScreenWidth;
        var screenHeight = SystemParameters.PrimaryScreenHeight;
        var windowRowSize = (int)(screenHeight / Height);
        var left = (int)(windowId / windowRowSize) * (int)Width;
        var top = (int)(windowId % windowRowSize) * (int)Height;
        Left = left;
        Top = top;
    }

    /// <summary>
    /// clear current plot and show new data with SignalConst Type for better performance  
    /// </summary>
    /// <param name="ys"></param>
    /// <param name="sampleTime">sample time, unit ms</param>
    public void ShowAllData(double[] ys, int sampleTime = 1)
    {
        _updatePlotTimer.Stop();
        LogPlot.Plot.Clear();
        //LogPlot.Plot.Axes.ContinuouslyAutoscale = false;
        var xs = Enumerable.Range(0, ys.Length)
            .Select(x => x * sampleTime).ToArray();
        LogPlot.Plot.Add.Palette = new ScottPlot.Palettes.Nord();
        _fullDataSignal = LogPlot.Plot.Add.SignalXY(xs, ys);

        _fullDataCrosshair = LogPlot.Plot.Add.Crosshair(0, 0);
        _fullDataCrosshair.IsVisible = false;
        _fullDataCrosshair.MarkerShape = MarkerShape.OpenCircle;
        _fullDataCrosshair.MarkerSize = 5;

        //LogPlot.Plot.XLabel("Time(ms)");
        //CustomPlotInteraction();
        LogPlot.Plot.Axes.AutoScale();
        LogPlot.Refresh();

        // wpf mouse move event, different from avalonia (PointerMoved)
        LogPlot.MouseMove += (s, e) =>
        {
            var currentPosition = e.GetPosition(LogPlot);
            // determine where the mouse is and get the nearest point
            Pixel mousePixel = new(currentPosition.X * LogPlot.DisplayScale, currentPosition.Y * LogPlot.DisplayScale);
            var mouseLocation = LogPlot.Plot.GetCoordinates(mousePixel);
            var nearest = _fullDataSignal.Data.GetNearest(mouseLocation,
                LogPlot.Plot.LastRender);

            switch (nearest.IsReal)
            {
                // place the crosshair over the highlighted point
                case true:
                    _fullDataCrosshair.IsVisible = true;
                    _fullDataCrosshair.Position = nearest.Coordinates;
                    LogPlot.Refresh();
                    Title = $"{LogName}: X={nearest.X:0.##}, Y={nearest.Y:0.##}";
                    break;
                // hide the crosshair when no point is selected
                case false when _fullDataCrosshair.IsVisible:
                    _fullDataCrosshair.IsVisible = false;
                    LogPlot.Refresh();
                    Title = $"{LogName}";
                    break;
            }
        };
    }

    public void Dispose() => _updatePlotTimer.Dispose();

    private void Window_PreviewLostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        var window = (Window)sender;
        window.Topmost = true;
    }
}