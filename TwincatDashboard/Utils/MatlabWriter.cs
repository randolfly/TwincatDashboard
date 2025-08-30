using System;
using System.Buffers.Binary;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace TwincatDashboard.Utils;

public static class MatlabWriter
{
    public static void WriteMatFileHeader(Stream stream)
    {
        // Write MAT-file header (128 bytes)
        var header = new byte[128];
        // 1. Description (max 116 bytes)
        var desc = Encoding.ASCII.GetBytes(
            "MATLAB 5.0 MAT-file, Platform: .NET, Created on: "
                + DateTime.Now.ToString("ddd MMM dd HH:mm:ss yyyy")
        );
        Array.Copy(desc, header, Math.Min(desc.Length, 116));
        // 2. Subsystem data offset (8 bytes, usually zero)
        // 3. Version (2 bytes) + Endian indicator (2 bytes)
        header[124] = 0x01; // version
        header[125] = 0x00; // version
        header[126] = (byte)'I'; // Little Endian indicator: 'IM'
        header[127] = (byte)'M';
        stream.Write(header, 0, 128);
    }

    public static void WriteMatrix(
        Stream stream,
        string name,
        ReadOnlySpan<double> data,
        int rows,
        int cols
    )
    {
        // --- Begin Data Element ---
        // Data type: miMATRIX  (14)
        // Write tag: type (4 bytes) + size (4 bytes)
        int totalBytes = CalcMatrixBytes(name, rows, cols);
        Span<byte> tag = stackalloc byte[8];
        BinaryPrimitives.WriteInt32LittleEndian(tag.Slice(0, 4), 14); // miMATRIX = 14
        BinaryPrimitives.WriteInt32LittleEndian(tag.Slice(4, 4), totalBytes);
        stream.Write(tag);

        // Array Flags
        Span<byte> flags = stackalloc byte[8];
        BinaryPrimitives.WriteInt32LittleEndian(flags.Slice(0, 4), 6); // miUINT32
        BinaryPrimitives.WriteInt32LittleEndian(flags.Slice(4, 4), 8); // 8 bytes
        stream.Write(flags);
        stream.Write(new byte[] { 0x00, 0x00, 0x00, 0x06, 0x00, 0x00, 0x00, 0x00 }); // mxDOUBLE_CLASS

        // Dimensions
        Span<byte> dimsTag = stackalloc byte[8];
        BinaryPrimitives.WriteInt32LittleEndian(dimsTag.Slice(0, 4), 5); // miINT32
        BinaryPrimitives.WriteInt32LittleEndian(dimsTag.Slice(4, 4), 8); // 2 dims * 4 bytes
        stream.Write(dimsTag);
        Span<byte> dims = stackalloc byte[8];
        BinaryPrimitives.WriteInt32LittleEndian(dims.Slice(0, 4), rows);
        BinaryPrimitives.WriteInt32LittleEndian(dims.Slice(4, 4), cols);
        stream.Write(dims);

        // Name
        var nameBytes = Encoding.ASCII.GetBytes(name);
        int nameLen = nameBytes.Length;
        Span<byte> nameTag = stackalloc byte[8];
        BinaryPrimitives.WriteInt32LittleEndian(nameTag.Slice(0, 4), 1); // miINT8
        BinaryPrimitives.WriteInt32LittleEndian(nameTag.Slice(4, 4), nameLen);
        stream.Write(nameTag);
        stream.Write(nameBytes, 0, nameLen);
        if (nameLen % 8 != 0)
            stream.Write(new byte[8 - (nameLen % 8)], 0, 8 - (nameLen % 8)); // padding

        // Data
        Span<byte> dataTag = stackalloc byte[8];
        BinaryPrimitives.WriteInt32LittleEndian(dataTag.Slice(0, 4), 9); // miDOUBLE
        BinaryPrimitives.WriteInt32LittleEndian(dataTag.Slice(4, 4), data.Length * 8);
        stream.Write(dataTag);

        // Write double data as bytes, no array copy
        var doubleSpan = MemoryMarshal.Cast<double, byte>(data);
        stream.Write(doubleSpan);

        if ((data.Length * 8) % 8 != 0)
            stream.Write(new byte[8 - ((data.Length * 8) % 8)], 0, 8 - ((data.Length * 8) % 8)); // padding
    }

    private static int CalcMatrixBytes(string name, int rows, int cols)
    {
        int nameLen = name.Length;
        int dataLen = rows * cols * 8;
        // 1. Array Flags tag+data (8+8)
        // 2. Dimensions tag+data (8+8)
        // 3. Name tag+data (8+pad)
        // 4. Data tag+data (8+pad)
        int namePad = (nameLen % 8 == 0) ? 0 : (8 - (nameLen % 8));
        int dataPad = ((dataLen % 8) == 0) ? 0 : (8 - (dataLen % 8));
        int total = 8 + 8 + 8 + (8 + nameLen + namePad) + (8 + dataLen + dataPad);
        return total;
    }
}
