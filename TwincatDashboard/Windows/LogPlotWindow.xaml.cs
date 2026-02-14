using ScottPlot;
using ScottPlot.Palettes;
using ScottPlot.Plottables;

using Serilog;

using System.Timers;
using System.Windows;
using System.Windows.Input;

using Timer = System.Timers.Timer;

namespace TwincatDashboard.Windows;

public partial class LogPlotWindow : Window {
  private readonly DataStreamer _dataStreamer;
  private readonly Timer _updatePlotTimer = new() { Interval = 50, AutoReset = true };
  private readonly ElapsedEventHandler _updatePlotHandler;
  private MouseEventHandler? _mouseMoveHandler;

  private Crosshair? _fullDataCrosshair;
  private Signal? _fullDataSignal;

  public LogPlotWindow(string title, int logNum) {
    InitializeComponent();

    LogName = title;
    Title = LogName;
    LogPlot.Plot.ScaleFactor = 1.0;

    _dataStreamer = LogPlot.Plot.Add.DataStreamer(logNum);
    _dataStreamer.ViewScrollLeft();

    _updatePlotHandler = OnUpdatePlotTimerElapsed;
    _updatePlotTimer.Elapsed += _updatePlotHandler;
    _updatePlotTimer.Start();

    Closed += OnWindowClosed;
  }

  private string LogName { get; }

  private void OnUpdatePlotTimerElapsed(object? sender, ElapsedEventArgs e) {
    if (_dataStreamer.HasNewData) LogPlot.Refresh();
    LogPlot.Plot.Axes.AutoScale();
  }

  private void OnWindowClosed(object? sender, EventArgs e) {
    Log.Information("Disposing LogPlotWindow: {LogName}", LogName);

    _updatePlotTimer.Elapsed -= _updatePlotHandler;
    _updatePlotTimer.Stop();
    _updatePlotTimer.Dispose();

    if (_mouseMoveHandler is not null)
      LogPlot.MouseMove -= _mouseMoveHandler;

    LogPlot.Plot.Clear();
    LogPlot.Refresh();

    _fullDataSignal = null;
    _fullDataCrosshair = null;
  }

  public void UpdatePlot(double newData) {
    _dataStreamer.Add(newData);
    LogPlot.Plot.GetPlottables<Marker>()
        .ToList()
        .ForEach(m => m.X -= 1);

    LogPlot.Plot.GetPlottables<Marker>()
        .Where(m => m.X < 0)
        .ToList()
        .ForEach(m => LogPlot.Plot.Remove(m));
  }

  public void SetPlotViewWindowPosById(int windowId) {
    var screenWidth = SystemParameters.PrimaryScreenWidth;
    var screenHeight = SystemParameters.PrimaryScreenHeight;
    var windowRowSize = (int)(screenHeight / Height);
    var left = windowId / windowRowSize * (int)Width;
    var top = windowId % windowRowSize * (int)Height;
    Left = left;
    Top = top;
  }

  public void ShowAllData(double[] ys, int sampleTime = 1) {
    _updatePlotTimer.Stop();
    LogPlot.Reset();
    LogPlot.Plot.Add.Palette = new Nord();

    _fullDataSignal = LogPlot.Plot.Add.SignalConst(ys, sampleTime);
    _fullDataCrosshair = LogPlot.Plot.Add.Crosshair(0, 0);
    _fullDataCrosshair.IsVisible = false;
    _fullDataCrosshair.MarkerShape = MarkerShape.OpenCircle;
    _fullDataCrosshair.MarkerSize = 5;

    LogPlot.Plot.Axes.AutoScale();
    LogPlot.Refresh();

    if (_mouseMoveHandler is not null)
      LogPlot.MouseMove -= _mouseMoveHandler;

    _mouseMoveHandler = OnPlotMouseMove;
    LogPlot.MouseMove += _mouseMoveHandler;
  }

  private void OnPlotMouseMove(object? sender, MouseEventArgs e) {
    if (_fullDataSignal is null || _fullDataCrosshair is null)
      return;

    var currentPosition = e.GetPosition(LogPlot);
    Pixel mousePixel = new(currentPosition.X * LogPlot.DisplayScale, currentPosition.Y * LogPlot.DisplayScale);
    var mouseLocation = LogPlot.Plot.GetCoordinates(mousePixel);
    var nearest = _fullDataSignal.GetNearest(mouseLocation, LogPlot.Plot.LastRender);

    switch (nearest.IsReal) {
      case true:
        _fullDataCrosshair.IsVisible = true;
        _fullDataCrosshair.Position = nearest.Coordinates;
        LogPlot.Refresh();
        Title = $"{LogName}: X={nearest.X:0.##}, Y={nearest.Y:0.##}";
        break;
      case false when _fullDataCrosshair.IsVisible:
        _fullDataCrosshair.IsVisible = false;
        LogPlot.Refresh();
        Title = $"{LogName}";
        break;
    }
  }

  private void Window_PreviewLostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e) {
    var window = (Window)sender;
    window.Topmost = true;
  }
}