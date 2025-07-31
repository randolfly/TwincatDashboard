using TwincatDashboard.Windows;

namespace TwincatDashboard.Services;

public class LogPlotService
{
    public Dictionary<string, LogPlotWindow> PlotDict { get; } = [];

    public void AddChannel(string channelName, int plotBufferCapacity)
    {
        var logPlotWindow = new LogPlotWindow(channelName, plotBufferCapacity);
        logPlotWindow.SetPlotViewWindowPosById(PlotDict.Count);
        PlotDict.Add(channelName, logPlotWindow);
        logPlotWindow.Show();
    }

    public void RemoveAllChannels()
    {
        foreach (var plot in PlotDict.Values)
        {
            plot.Close();
        }

        PlotDict.Clear();
    }

    public void AddData(string channelName, double data)
    {
        if (PlotDict.TryGetValue(channelName, value: out var value))
        {
            value.UpdatePlot(data);
        }
    }

    public void ShowAllChannelsWithNewData(
        Dictionary<string, double[]> dataSrcDict,
        int dataLength,
        int sampleTime = 1
    )
    {
        foreach (var (channelName, data) in dataSrcDict)
        {
            if (PlotDict.TryGetValue(channelName, out var value))
            {
                value.ShowAllData(data[..dataLength], sampleTime);
            }
        }
    }

}
