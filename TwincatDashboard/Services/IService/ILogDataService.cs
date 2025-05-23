﻿using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using TwincatDashboard.Models;
using TwincatDashboard.Utils;

namespace TwincatDashboard.Services.IService;

public interface ILogDataService
{
    public Dictionary<string, List<double>> SlowLogDict { get; }
    public Dictionary<string, LogDataChannel> QuickLogDict { get; }

    public void AddChannel(string channelName);
    public Task AddDataAsync(string channelName, double data);
    public void RemoveAllChannels();
    public Task<Dictionary<string, List<double>>> LoadAllChannelsAsync();
    
    public void RegisterSlowLog(string channelName);
    public void AddSlowLogData(string channelName, double data);
    public void RemoveAllSlowLog();
    
    public Task ExportDataAsync(Dictionary<string, List<double>> dataSrc, string fileName, List<string> exportTypes);
    public void DeleteTmpFiles();
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

    public async Task AddAsync(double data)
    {
        _buffer.Add(data);
        if ((_buffer.Size * 2) >= _buffer.Capacity)
        {
            Debug.WriteLine($"Buffer is half size, save to file: {FilePath}");
            var dataSrc = _buffer.RemoveRange(_buffer.Size);
            await SaveToFileAsync(dataSrc, FilePath);
        }
    }

    private static async Task SaveToFileAsync(ArraySegment<double> array, string filePath)
    {
        var stringBuilder = new StringBuilder();
        foreach (var value in array)
        {
            stringBuilder.AppendLine(value.ToString(CultureInfo.InvariantCulture));
        }

        await using var fileStream = new FileStream(filePath,
            FileMode.Append,
            FileAccess.Write,
            FileShare.None, 4096, true);
        await using var writer = new StreamWriter(fileStream);
        await writer.WriteAsync(stringBuilder.ToString());
    }

    public async Task<List<double>> LoadFromFileAsync()
    {
        var data = new List<double>();
        if (!File.Exists(FilePath))
        {
            return data;
        }

        await using var fileStream = new FileStream(FilePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read, 4096, true);
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

    public void DeleteTmpFile()
    {
        if (File.Exists(FilePath))
        {
            File.Delete(FilePath);
        }
    }
}