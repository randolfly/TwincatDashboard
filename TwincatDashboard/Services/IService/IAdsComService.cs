using TwinCAT.Ads;
using TwincatDashboard.Models;

namespace TwincatDashboard.Services.IService;

public interface IAdsComService : IDisposable
{

    /// <summary>
    /// Ads连接状态
    /// </summary>
    /// <value>true: 连接上; false: 未连接</value>
    public bool IsAdsConnected { get; }

    public AdsState GetAdsState();
    public void ConnectAdsServer(AdsConfig adsConfig);
    public Task ConnectAdsServerAsync(AdsConfig adsConfig);
    public void DisconnectAdsServer();
    public Task DisconnectAdsServerAsync();
    public List<SymbolInfo> GetAvailableSymbols();
    public Task<object?> ReadPlcSymbolValueAsync(string symbolPath, Type type);
    public Task<bool> WritePlcSymbolValueAsync<T>(string symbolPath, T value);
    
    /// <summary>
    /// Reads the task cycle time from the PLC(unit: ms), which determines the <cref>LogConfig.QuickLogPeriod</cref> time in the application.
    /// </summary>
    /// <returns>task cycle time, unit ms.</returns>
    public Task<int> GetTaskCycleTimeAsync();
    
    public List<AdsRouteInfo> ScanAdsRoutes();
    public void AddNotificationHandler(EventHandler<AdsNotificationEventArgs> handler);
    public void RemoveNotificationHandler(EventHandler<AdsNotificationEventArgs> handler);
    public uint AddDeviceNotification(string path, int byteSize, NotificationSettings settings);

    public void RemoveDeviceNotification(uint notificationHandle);
}

public class AdsRouteInfo {
    public required string Name { get; set; }
    public required string Address { get; set; }
    public required string NetId { get; set; }
}