using System.Buffers;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;

using MathNet.Numerics.Data.Matlab;
using MathNet.Numerics.LinearAlgebra;

using TwincatDashboard.Models;
using TwincatDashboard.Utils;

namespace TwincatDashboard.Services;

public class LogDataService
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


public class LogDataChannel(int bufferCapacity, string channelName)
{
    private string Name { get; set; } = channelName;
    public string? Description { get; set; }
    public int BufferCapacity { get; set; } = bufferCapacity;

    private static string LogDataTempFolder
    {
        get
        {
            var path = Path.Combine(AppConfig.FolderName, AppConfig.FolderName, "tmp/");
            if (!Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
            }

            return path;
        }
    }

    private string FilePath => Path.Combine(LogDataTempFolder, "_" + Name + ".csv");

    // storage tmp data for logging(default data type is double)
    private readonly CircularBuffer<double> _buffer = new(bufferCapacity);

    public async Task AddAsync(double data) {
        _buffer.Add(data);
        if ((_buffer.Size * 2) >= _buffer.Capacity)
        {
            Debug.WriteLine($"Buffer is half size, save to file: {FilePath}");
            var dataSrc = _buffer.RemoveRange(_buffer.Size);
            await SaveToFileAsync(dataSrc, FilePath);
        }
    }

    private static async Task SaveToFileAsync(ArraySegment<double> array, string filePath) {
        var stringBuilder = new StringBuilder();
        foreach (var value in array)
        {
            stringBuilder.AppendLine(value.ToString(CultureInfo.InvariantCulture));
        }

        await using var fileStream = new FileStream(
            filePath,
            FileMode.Append,
            FileAccess.Write,
            FileShare.None,
            4096,
            true
        );
        await using var writer = new StreamWriter(fileStream);
        await writer.WriteAsync(stringBuilder.ToString());
    }

    private static ArrayPool<double> ArrayPool => ArrayPool<double>.Shared;

    public static void ReturnArray(double[] doubles) => ArrayPool.Return(doubles);

    public async Task<List<double>> LoadFromFileAsync() {
        var data = new List<double>();
        if (!File.Exists(FilePath))
        {
            return data;
        }

        await using var fileStream = new FileStream(
            FilePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            4096,
            true
        );
        using var reader = new StreamReader(fileStream);
        while (await reader.ReadLineAsync() is { } line)
        {
            if (double.TryParse(line, out var value))
            {
                data.Add(value);
            }
        }

        return data;
    }

    public async Task<double[]> LoadArrayPoolFromFileAsync() {
        // if file not exists, return empty array pool
        if (!File.Exists(FilePath))
        {
            return ArrayPool.Rent(0);
        }

        // get file lines number
        var fileData = await File.ReadAllLinesAsync(FilePath);
        var arrayLength = fileData.Count();
        var data = ArrayPool.Rent(arrayLength);
        for (int i = 0; i < arrayLength; i++)
        {
            if (double.TryParse(fileData[i], out var value))
            {
                data[i] = value;
            }
            else
            {
                data[i] = 0;
            }
        }

        return data;
    }

    public void DeleteTmpFile() {
        if (File.Exists(FilePath))
        {
            File.Delete(FilePath);
        }
    }
}