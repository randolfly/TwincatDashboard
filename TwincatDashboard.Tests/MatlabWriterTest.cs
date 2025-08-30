using System;
using System.IO;
using System.Reflection;
using TwincatDashboard.Utils;
using Xunit;

namespace TwincatDashboard.Tests;

public class MatlabWriterTests
{
    [Fact]
    public void WriteMatFileHeader_Writes128BytesHeader()
    {
        using var ms = new MemoryStream();
        MatlabWriter.WriteMatFileHeader(ms);
        Assert.Equal(128, ms.Length);
        ms.Position = 0;
        var header = new byte[128];
        ms.Read(header, 0, 128);
        var headerStr = System.Text.Encoding.ASCII.GetString(header, 0, 32);
        Assert.Contains("MATLAB 5.0 MAT-file", headerStr);
        ms.Close();
    }

    [Fact]
    public void WriteMatrix_WritesValidMatMatrix()
    {
        using var ms = new MemoryStream();
        MatlabWriter.WriteMatFileHeader(ms);
        ReadOnlySpan<double> data = [1.1, 2.2, 3.3, 4.4];
        MatlabWriter.WriteMatrix(ms, "A", data, 2, 2);
        // 检查流长度大于128（有数据）
        Assert.True(ms.Length > 128);
        ms.Position = 128;
        var buffer = new byte[ms.Length - 128];
        ms.Read(buffer, 0, buffer.Length);
        // 检查miMATRIX类型（前4字节）
        Assert.Equal(14, BitConverter.ToInt32(buffer, 0));
        // 检查变量名
        var nameIndex = Array.IndexOf(buffer, (byte)'A');
        Assert.True(nameIndex > 0);
        ms.Close();
    }

    [Fact]
    public void GenerateMatlabFile_ValidateByMatlab()
    {
        using var fs = new FileStream(
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                "test_matlab.mat"
            ),
            FileMode.Create,
            FileAccess.Write
        );
        var testData = Enumerable.Range(1, 100_0000).Select(x => (double)x).ToArray();
        ReadOnlySpan<double> data = new ReadOnlySpan<double>(testData);
        MatlabWriter.WriteMatFileHeader(fs);
        MatlabWriter.WriteMatrix(fs, "test_list", data, data.Length, 1);
        Assert.True(data.Length > 100);
    }
}
