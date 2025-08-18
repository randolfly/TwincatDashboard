using System.Buffers;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Shell;

using MathNet.Numerics.Data.Matlab;
using MathNet.Numerics.LinearAlgebra;
using MathNet.Numerics.LinearAlgebra.Double;

using Serilog;

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

    public async Task<Dictionary<string, double[]>> LoadAllChannelsAsync() {
        var resultDict = new Dictionary<string, double[]>();
        foreach (var channel in QuickLogDict)
        {
            await channel.Value.LoadFromFileByArrayPoolAsync();
            resultDict.Add(channel.Key, channel.Value.LogData);
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
    /// <param name="dataLength">actual data length</param>
    /// <returns></returns>
    public async Task ExportDataAsync(Dictionary<string, double[]> dataSrc, string fileName, List<string> exportTypes, int dataLength) {
        if (exportTypes.Contains("csv"))
        {
            var fileStream = new FileStream(fileName + ".csv", FileMode.Create, FileAccess.Write, FileShare.None);
            await using var writer = new StreamWriter(fileStream, Encoding.UTF8);
            // Write header
            await writer.WriteLineAsync(string.Join(',', dataSrc.Keys));
            var rowCount = dataLength;
            for (var i = 0; i < rowCount; i++)
            {
                var row = new List<string>();
                foreach (var channel in dataSrc)
                {
                    row.Add(channel.Value.ElementAt(i).ToString(CultureInfo.InvariantCulture));
                }
                await writer.WriteLineAsync(string.Join(',', row));
            }
        }

        if (exportTypes.Contains("mat"))
        {
            var exportMatDict = new Dictionary<string, Matrix<double>>();
            foreach (var keyValuePair in dataSrc)
            {
                // TODO: The Matrix.Build.Dense method require an array instead of a Span, which leads to further array copy
                //var arraySlice = new ReadOnlySpan<double>(
                //    keyValuePair.Value, 0, dataLength);
                exportMatDict.Add(
                FormatNameForMatFile(keyValuePair.Key),
                Matrix<double>.Build.Dense(
                        keyValuePair.Value.Length,
                        1,
                        keyValuePair.Value
                    )
                );
                DenseVector.Build.DenseOfArray(keyValuePair.Value);
            }

            await Task.Run(() => MatlabWriter.Write(fileName + ".mat", exportMatDict));
        }

        static string FormatNameForMatFile(string symbolName) {
            return symbolName
                .Replace("TwinCAT_SystemInfoVarList._TaskInfo[1].", "Task")
                .Replace(".", "_")
                .Replace("[", "_")
                .Replace("]", "_");
        }
    }

    public void DeleteTmpFiles() {
        QuickLogDict.Values.ToList().ForEach(channel => channel.DeleteTmpFile());
    }
}


public class LogDataChannel(int bufferCapacity, string channelName) : IDisposable
{
    private string Name { get; set; } = channelName;
    public string? Description { get; set; }
    public int BufferCapacity { get; set; } = bufferCapacity;
    public int DataLength { get; private set; } = 0;

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
    private readonly CircularBuffer<double> ChannelBuffer = new(bufferCapacity);

    // Use ArrayPool to rent and return arrays for performance optimization
    private static ArrayPool<double> ArrayPool => ArrayPool<double>.Shared;
    public double[] LogData { get; private set; } = ArrayPool.Rent(0);

    public async Task AddAsync(double data) {
        ChannelBuffer.Add(data);
        DataLength++;
        if ((ChannelBuffer.Size * 2) >= ChannelBuffer.Capacity)
        {
            Debug.WriteLine($"Buffer is half size, save to file: {FilePath}");
            var dataSrc = ChannelBuffer.RemoveRange(ChannelBuffer.Size);
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

    public async Task LoadFromFileByArrayPoolAsync() {
        // if file not exists, return empty array pool
        if (!File.Exists(FilePath))
        {
            LogData = ArrayPool.Rent(0);
            return;
        }

        LogData = ArrayPool.Rent(DataLength);
        // use a stream to read file by lines

        await using var fileStream = new FileStream(
            FilePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            4096,
            true);

        using var reader = new StreamReader(fileStream);
        var index = 0;
        while (await reader.ReadLineAsync() is { } line)
        {
            if (double.TryParse(line, out var value))
            {
                if (index >= LogData.Length) break;
                LogData[index] = value;
            }
            else
            {
                Log.Warning("Failed to parse line: {Line} of {File}", line, FilePath);
                LogData[index] = 0;
            }
            index++;
        }
        // update DataLength if actual data length is less than expected(file IO latency may cause this)
        if (index <= DataLength) DataLength = index;
        for (var i = DataLength; i < LogData.Length; i++)
        {
            LogData[i] = LogData[DataLength - 1]; // fill the rest with last value
        }
        Log.Information("{File} ideal data length: {DataLength}, actual data length: {ActualLength}",
            FilePath, LogData.Length, index);

        Log.Information("Retrieve all data from file {FilePath}", FilePath);
    }

    public void ReturnArrayToPool() {
        if (LogData is not null && LogData.Length > 0)
        {
            ArrayPool.Return(LogData);
            LogData = [];
        }
        ChannelBuffer.ReturnBufferToArrayPool();
    }

    public void DeleteTmpFile() {
        if (File.Exists(FilePath))
        {
            File.Delete(FilePath);
        }
    }

    public void Dispose() {
        ReturnArrayToPool();
    }
}