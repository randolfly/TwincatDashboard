


using TwincatDashboard.Services.IService;
using TwincatDashboard.Windows;

namespace TwincatDashboard.Services;
public class LogPlotService : ILogPlotService
{
    public Dictionary<string, LogPlotWindow> PlotDict { get; } = [];

    public void AddChannel(string channelName, int plotBufferCapacity) {
        var logPlotWindow = new LogPlotWindow(channelName, plotBufferCapacity);
        logPlotWindow.SetPlotViewWindowPosById(PlotDict.Count);
        PlotDict.Add(channelName, logPlotWindow);
        logPlotWindow.Show();
    }

    public void RemoveAllChannels() {
        foreach (var plot in PlotDict.Values)
        {
            plot.Close();
        }
        PlotDict.Clear();
    }

    public void AddData(string channelName, double data) {
        if (PlotDict.TryGetValue(channelName, value: out var value))
        {
            value.UpdatePlot(data);
        }
    }

    public void ShowAllChannelsWithNewData(Dictionary<string, List<double>> dataSrcDict, int sampleTime = 1) {
        foreach (var (channelName, data) in dataSrcDict)
        {
            if (PlotDict.TryGetValue(channelName, out var value))
            {
                value.ShowAllData(data.ToArray(), sampleTime);
            }
        }
    }
}