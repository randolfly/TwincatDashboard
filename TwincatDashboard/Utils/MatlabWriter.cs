using System.Buffers.Binary;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace TwincatDashboard.Utils;

public static class MatlabWriter {
    public static void WriteMatFileHeader(Stream stream) {
        // Write MAT-file header (128 bytes)
        var header = new byte[128];
        // 1. Description (max 116 bytes)
        var description = Encoding.ASCII.GetBytes(
            "MATLAB 5.0 MAT-file, Platform: .NET, Created on: "
                + DateTime.Now.ToString("yyyy.MM.dd HH:mm:ss")
        );
        Array.Copy(description, header, Math.Min(description.Length, 116));
        // 2. Subsystem data offset (8 bytes, usually zero)
        // 3. Version (2 bytes) + Endian indicator (2 bytes)
        header[124] = 0x00; // version
        header[125] = 0x01; // version
        header[126] = (byte)'I'; // Little Endian indicator: 'IM'
        header[127] = (byte)'M';
        stream.Write(header, 0, 128);
        stream.Flush();
    }

    /// <summary>
    /// Write a 1D Array to the MAT-file stream.
    /// </summary>
    /// <param name="stream"></param>
    /// <param name="name"></param>
    /// <param name="data"></param>
    /// <param name="rows"></param>
    public static void WriteArray(
        Stream stream,
        string name,
        ReadOnlySpan<double> data,
        int rows
    ) {
        // --- Begin Data Element ---
        int totalBytes = CalMatrixBytes(name, rows, 1);
        Span<byte> tag = stackalloc byte[8];
        BinaryPrimitives.WriteInt32LittleEndian(tag[..4], 14); // miMATRIX = 14
        BinaryPrimitives.WriteInt32LittleEndian(tag[4..8], totalBytes);
        stream.Write(tag);

        // Array Flags
        Span<byte> flagsTag = stackalloc byte[8];
        BinaryPrimitives.WriteInt32LittleEndian(flagsTag[..4], 6); // miUINT32
        BinaryPrimitives.WriteInt32LittleEndian(flagsTag[4..8], 8); // 8 bytes
        stream.Write(flagsTag);

        Span<byte> flagsData = stackalloc byte[8];
        BinaryPrimitives.WriteInt32LittleEndian(flagsData[..4], 6); // mxDOUBLE_CLASS
        BinaryPrimitives.WriteInt32LittleEndian(flagsData[4..8], 0); // flags
        stream.Write(flagsData);

        // Dimensions
        Span<byte> dimsTag = stackalloc byte[8];
        BinaryPrimitives.WriteInt32LittleEndian(dimsTag[..4], 5); // miINT32
        BinaryPrimitives.WriteInt32LittleEndian(dimsTag[4..8], 8); // 2 dims * 4 bytes
        stream.Write(dimsTag);

        Span<byte> dims = stackalloc byte[8];
        BinaryPrimitives.WriteInt32LittleEndian(dims[..4], rows);
        BinaryPrimitives.WriteInt32LittleEndian(dims[4..8], 1);
        stream.Write(dims);

        // Name
        var nameBytes = Encoding.ASCII.GetBytes(name);
        int nameLen = nameBytes.Length;
        Span<byte> nameTag = stackalloc byte[8];
        BinaryPrimitives.WriteInt32LittleEndian(nameTag[..4], 1); // miINT8
        BinaryPrimitives.WriteInt32LittleEndian(nameTag[4..8], nameLen);
        stream.Write(nameTag);
        stream.Write(nameBytes, 0, nameLen);
        if (nameLen % 8 != 0)
            stream.Write(new byte[8 - (nameLen % 8)], 0, 8 - (nameLen % 8)); // padding

        // Data
        Span<byte> dataTag = stackalloc byte[8];
        BinaryPrimitives.WriteInt32LittleEndian(dataTag[..4], 9); // miDOUBLE
        BinaryPrimitives.WriteInt32LittleEndian(dataTag[4..8], data.Length * 8);
        stream.Write(dataTag);

        // Write double data as bytes, no array copy
        var doubleSpan = MemoryMarshal.Cast<double, byte>(data);
        stream.Write(doubleSpan);

        stream.Flush();
    }

    private static int CalMatrixBytes(string name, int rows, int cols) {
        int nameLen = name.Length;
        int dataLen = rows * cols * 8;
        // 1. Array Flags tag+data (8+8)
        // 2. Dimensions tag+data (8+8)
        // 3. Name tag+data (8+len+pad)
        // 4. Data tag+data (8+len), pad=0 since double is 8 bytes
        int namePad = (nameLen % 8 == 0) ? 0 : (8 - (nameLen % 8));
        int total = (8 + 8) + (8 + 8) + (8 + nameLen + namePad) + (8 + dataLen);
        return total;
    }
}
