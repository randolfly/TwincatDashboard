using System.Globalization;
using System.IO;
using System.Text;

using MathNet.Numerics.Data.Matlab;
using MathNet.Numerics.LinearAlgebra;

using TwincatDashboard.Services.IService;

namespace TwincatDashboard.Services;

public class LogDataService : ILogDataService
{
    private static int BufferCapacity => 1000;

    public Dictionary<string, List<double>> SlowLogDict { get; } = [];
    public Dictionary<string, LogDataChannel> QuickLogDict { get; } = [];

    public void AddChannel(string channelName) {
        QuickLogDict.Add(channelName, new LogDataChannel(BufferCapacity, channelName));
    }

    public void RemoveAllChannels() {
        QuickLogDict.Clear();
    }

    public async Task AddDataAsync(string channelName, double data) {
        if (QuickLogDict.TryGetValue(channelName, out var value))
        {
            await value.AddAsync(data);
        }
    }

    public async Task<List<double>> LoadDataAsync(string channelName) {
        return await QuickLogDict[channelName].LoadFromFileAsync();
    }

    public async Task<Dictionary<string, List<double>>> LoadAllChannelsAsync() {
        var resultDict = new Dictionary<string, List<double>>();
        foreach (var channel in QuickLogDict)
        {
            resultDict.Add(channel.Key, await channel.Value.LoadFromFileAsync());
        }

        var minCount = resultDict.Min(x => x.Value.Count);
        foreach (var channel in resultDict)
        {
            channel.Value.RemoveRange(minCount, channel.Value.Count - minCount);
        }

        return resultDict;
    }

    public void RegisterSlowLog(string channelName) {
        SlowLogDict.Add(channelName, []);
    }

    public void AddSlowLogData(string channelName, double data) {
        if (SlowLogDict.TryGetValue(channelName, out var value))
        {
            value.Add(data);
        }
    }

    public void RemoveAllSlowLog() {
        foreach (var channel in SlowLogDict)
        {
            channel.Value.Clear();
        }

        SlowLogDict.Clear();
    }

    /// <summary>
    /// export data to file
    /// </summary>
    /// <param name="dataSrc"></param>
    /// <param name="fileName">export file full name, doesn't contain suffix, such as "c:/FOLDER/aaa"</param>
    /// <param name="exportTypes"></param>
    /// <returns></returns>
    public async Task ExportDataAsync(
        Dictionary<string, List<double>> dataSrc,
        string fileName,
        List<string> exportTypes
    ) {
        if (exportTypes.Contains("csv"))
        {
            var stringBuilder = new StringBuilder();
            stringBuilder.AppendLine(string.Join(',', dataSrc.Keys));
            var rowCount = dataSrc.First().Value.Count;
            for (var i = 0; i < rowCount; i++)
            {
                var row = new List<string>();
                foreach (var channel in dataSrc)
                {
                    row.Add(channel.Value[i].ToString(CultureInfo.InvariantCulture));
                }

                stringBuilder.AppendLine(string.Join(',', row));
            }

            await File.WriteAllTextAsync(fileName + ".csv", stringBuilder.ToString());
        }

        if (exportTypes.Contains("mat"))
        {
            var exportMatDict = new Dictionary<string, Matrix<double>>();
            foreach (var keyValuePair in dataSrc)
            {
                exportMatDict.Add(
                    keyValuePair
                        .Key.Replace("TwinCAT_SystemInfoVarList._TaskInfo[1].", "Task")
                        .Replace(".", "_")
                        .Replace("[", "_")
                        .Replace("]", "_"),
                    Matrix<double>.Build.Dense(
                        keyValuePair.Value.Count,
                        1,
                        keyValuePair.Value.ToArray()
                    )
                );
            }

            await Task.Run(() => MatlabWriter.Write(fileName + ".mat", exportMatDict));
        }
    }

    public void DeleteTmpFiles() {
        QuickLogDict.Values.ToList().ForEach(channel => channel.DeleteTmpFile());
    }
}
