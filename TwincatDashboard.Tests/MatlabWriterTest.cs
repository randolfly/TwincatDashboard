using TwincatDashboard.Utils;

namespace TwincatDashboard.Tests;

public class MatlabWriterTests {
    [Fact]
    public void WriteMatFileHeader_Writes128BytesHeader() {
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
    public void GenerateMatlabFile_ValidateByMatlab() {
        using var fs = new FileStream(
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                "test_matlab.mat"
            ),
            FileMode.Create,
            FileAccess.Write
        );
        var testData = Enumerable.Range(10_0000_0000, 10).Select(x => (double)x).ToArray();
        ReadOnlySpan<double> data = new ReadOnlySpan<double>(testData);
        MatlabWriter.WriteMatFileHeader(fs);
        MatlabWriter.WriteArray(fs, "test_list", data, data.Length);
        MatlabWriter.WriteArray(fs, "test_list_1", data, 3);
        MatlabWriter.WriteArray(fs, "test_list_2", data, 5);

        Assert.True(data.Length > 2);
    }
}