using System.Diagnostics;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;
using Serilog;
using TwinCAT;
using TwinCAT.Ads;
using TwinCAT.Ads.TypeSystem;
using TwinCAT.TypeSystem;
using TwinCAT.ValueAccess;
using TwincatDashboard.Constants;
using TwincatDashboard.Models;
using TwincatDashboard.Services.IService;

namespace TwincatDashboard.Services;

public class AdsComService() : IAdsComService
{
    private readonly int _cancelTimeout = 2000;
    public bool IsAdsConnected => _adsClient.IsConnected;

    private readonly AdsClient _adsClient = new();

    public AdsState GetAdsState()
    {
        var adsState = AdsState.Invalid;
        try
        {
            var stateInfo = _adsClient.ReadState();
            adsState = stateInfo.AdsState;
            Debug.WriteLine("ADS State: {0}, Device State: {1}", stateInfo.AdsState, stateInfo.DeviceState);
        }
        catch (AdsErrorException ex)
        {
            Debug.WriteLine("ADS Error: {0}", ex.Message);
        }
        catch (Exception ex)
        {
            Debug.WriteLine("General Exception: {0}", ex.Message);
        }

        return adsState;
    }

    public void ConnectAdsServer(AdsConfig adsConfig)
    {
        var amsAddress = new AmsAddress(adsConfig.NetId, adsConfig.PortId);
        _adsClient.Connect(amsAddress);
        Log.Information("Ads server connected: {NetId}:{PortId}", adsConfig.NetId, adsConfig.PortId);
        Log.Information("Ads server state: {AdsState}", GetAdsState());
    }

    public async Task ConnectAdsServerAsync(AdsConfig adsConfig)
    {
        var cts = new CancellationTokenSource();
        cts.CancelAfter(_cancelTimeout);
        var amsAddress = new AmsAddress(adsConfig.NetId, adsConfig.PortId);
        try
        {
            await _adsClient.ConnectAsync(amsAddress, cts.Token);
            Log.Information("Ads server connected: {NetId}:{PortId}", adsConfig.NetId, adsConfig.PortId);
            Log.Information("Ads server state: {AdsState}", GetAdsState());
        }
        catch (OperationCanceledException e)
        {
            Console.WriteLine(e);
            Log.Error(e, "Connect Ads server timeout");
        }
        finally
        {
            cts.Dispose();
        }
    }

    public void DisconnectAdsServer()
    {
        _adsClient.Disconnect();
        Debug.WriteLine("Ads server state: {0}", GetAdsState());
    }

    public async Task DisconnectAdsServerAsync()
    {
        if (!IsAdsConnected) return;
        var cts = new CancellationTokenSource();
        cts.CancelAfter(_cancelTimeout);
        try
        {
            await _adsClient.DisconnectAsync(cts.Token);
            Log.Information("Ads server state: {AdsState}", GetAdsState());
        }
        catch (OperationCanceledException e)
        {
            Log.Error(e, "Disconnect Ads server timeout");
        }
        finally
        {
            cts.Dispose();
        }
    }

    /// <summary>
    /// 获取所有可行的Symbols
    /// 1. 通过SymbolLoader加载所有Symbols
    /// 2. 递归遍历所有Symbols(仅获取MAIN和GVL下的Symbols)
    /// 3. 将Symbol转化为SymbolTree，同时执行重复项剔除
    /// 4. 将SymbolTree转化为<see cref="List{SymbolInfo}"/>，返回
    /// </summary>
    /// <returns>
    ///     <see cref="List{SymbolInfo}"/>
    /// </returns>
    public List<SymbolInfo> GetAvailableSymbols()
    {
        var settings = new SymbolLoaderSettings(SymbolsLoadMode.VirtualTree,
            ValueAccessMode.SymbolicByHandle);
        var symbolLoader = SymbolLoaderFactory.Create(_adsClient, settings);
        var symbols = symbolLoader.Symbols;

        var symbolList = new List<SymbolInfo>();

        foreach (var symbol in symbols)
        {
            if (AdsConstants.ReadNamespace.Contains(symbol.InstanceName))
            {
                symbolList.AddRange(LoadSymbolTreeBfs(symbol));
            }
        }

        return symbolList;

        List<SymbolInfo> LoadSymbolTreeBfs(ISymbol root)
        {
            var symbolInfos = new List<SymbolInfo>();
            var transverseOrder = new Queue<ISymbol>();

            var symbolLoadQueue = new Queue<ISymbol>();
            symbolLoadQueue.Enqueue(root);


            while (symbolLoadQueue.Count > 0)
            {
                var currentSymbol = symbolLoadQueue.Dequeue();
                transverseOrder.Enqueue(currentSymbol);
                foreach (var subSymbol in currentSymbol.SubSymbols)
                {
                    if (!subSymbol.IsReference)
                    {
                        symbolLoadQueue.Enqueue(subSymbol);
                    }
                }
            }

            while (transverseOrder.Count > 0)
            {
                var symbol = transverseOrder.Dequeue();
                if (symbol.SubSymbols.Count == 0)
                {
                    symbolInfos.Add(new SymbolInfo(symbol));
                }
            }

            return symbolInfos;
        }
    }

    public async Task<object?> ReadPlcSymbolValueAsync(string symbolPath, Type type)
    {
        if (string.IsNullOrEmpty(symbolPath)) return null;
        var resultHandle = await _adsClient.CreateVariableHandleAsync(symbolPath, CancellationToken.None);
        var varHandle = resultHandle.Handle;
        if (!resultHandle.Succeeded)
        {
            Log.Error("Failed to create variable handle for {SymbolPath} with error: {ErrorCode}", symbolPath, resultHandle.ErrorCode);
            return null;
        }

        try
        {
            var resultRead = await _adsClient
                .ReadAnyAsync(varHandle, type, CancellationToken.None);
            return resultRead.Succeeded ? resultRead.Value : null;
        }
        catch (Exception e)
        {
            Log.Error(e, "Failed to read plc symbol value: {SymbolPath}", symbolPath);
        }
        finally
        {
            await _adsClient.DeleteVariableHandleAsync(varHandle, CancellationToken.None);
        }

        return null;
    }

    public async Task<bool> WritePlcSymbolValueAsync<T>(string symbolPath, T value)
    {
        if (string.IsNullOrEmpty(symbolPath) || value == null) return false;
        var resultHandle = await _adsClient
            .CreateVariableHandleAsync(symbolPath, CancellationToken.None);
        var varHandle = resultHandle.Handle;
        if (!resultHandle.Succeeded)
        {
            Log.Error("Failed to create variable handle for {SymbolPath} with error: {ErrorCode}", symbolPath, resultHandle.ErrorCode);
            return false;
        }

        try
        {
            var resultWrite = await _adsClient
                .WriteAnyAsync(varHandle, value, CancellationToken.None);
            return resultWrite.Succeeded;
        }
        catch (Exception e)
        {
            Log.Error(e, "Failed to write plc symbol {SymbolPath} with value {Value}", symbolPath, value);
        }
        finally
        {
            await _adsClient.DeleteVariableHandleAsync(varHandle, CancellationToken.None);
        }

        return false;
    }

    public async Task<int> GetTaskCycleTimeAsync()
    {
        if (!IsAdsConnected) return 1;
        var cycleTime = await ReadPlcSymbolValueAsync(AdsConstants.TaskCycleTimeName, typeof(uint)) as uint?;
        return cycleTime switch
        {
            null => 1,
            > 0 => (int)(cycleTime / 10000),
            _ => 1
        };
    }

    public void Dispose() => _adsClient.Dispose();

    public void AddNotificationHandler(EventHandler<AdsNotificationEventArgs> handler) =>
        _adsClient.AdsNotification += handler;

    public void RemoveNotificationHandler(EventHandler<AdsNotificationEventArgs> handler) =>
        _adsClient.AdsNotification -= handler;

    public uint AddDeviceNotification(string path, int byteSize, NotificationSettings settings)
    {
        _adsClient.TryAddDeviceNotification(path, byteSize, settings, null, out var notificationHandle);
        return notificationHandle;
    }

    public void RemoveDeviceNotification(uint notificationHandle) =>
        _adsClient.TryDeleteDeviceNotification(notificationHandle);

    public List<AdsRouteInfo> ScanAdsRoutes()
    {
        var xml = XDocument.Load(AppConstants.AdsRouteXmlPath);
        var routeList = xml?.Root?.Element("RemoteConnections")?.Elements()
            .Select(route => new AdsRouteInfo
            {
                Name = route.Element("Name")?.Value ?? string.Empty,
                Address = route.Element("Address")?.Value ?? string.Empty,
                NetId = route.Element("NetId")?.Value ?? string.Empty
            }).ToList()!;

        routeList.Add(new AdsRouteInfo
        {
            Name = "Local",
            Address = AmsNetId.Local.ToString(),
            NetId = AmsNetId.Local.ToString()
        });
        return routeList;
    }
}