using TwincatDashboard.Windows;

namespace TwincatDashboard.Services.IService;

public interface ILogPlotService
{
    
    public Dictionary<string, LogPlotWindow> PlotDict { get; }

    public void AddChannel(string channelName, int plotBufferCapacity);
    public void AddData(string channelName, double data);
    public void RemoveAllChannels();
    public void ShowAllChannelsWithNewData(Dictionary<string, List<double>> dataSrcDict, int sampleTime);
}