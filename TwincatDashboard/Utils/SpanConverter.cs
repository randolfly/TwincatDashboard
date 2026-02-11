using System.Runtime.InteropServices;

namespace TwincatDashboard.Utils;

public static class SpanConverter
{
    public static T ConvertTo<T>(ReadOnlySpan<byte> span)
        where T : struct
    {
        if (span.Length < Marshal.SizeOf<T>())
            throw new ArgumentException("The span is too small to contain the specified type.");

        return MemoryMarshal.Read<T>(span);
    }

    public static object ConvertTo(ReadOnlySpan<byte> span, Type targetType)
    {
        if (span.IsEmpty || span.Length == 0) throw new ArgumentException("Span is null or empty.", nameof(span));

        ArgumentNullException.ThrowIfNull(targetType);

        switch (Type.GetTypeCode(targetType))
        {
            case TypeCode.Byte:
                if (span.Length >= sizeof(byte)) return span[0];
                break;
            case TypeCode.Boolean:
                if (span.Length >= sizeof(bool)) return BitConverter.ToBoolean(span);
                break;
            case TypeCode.UInt16:
                if (span.Length >= sizeof(ushort)) return BitConverter.ToUInt16(span);
                break;
            case TypeCode.UInt32:
                if (span.Length >= sizeof(uint)) return BitConverter.ToUInt32(span);
                break;
            case TypeCode.UInt64:
                if (span.Length >= sizeof(ulong)) return BitConverter.ToUInt64(span);
                break;
            case TypeCode.Int16:
                if (span.Length >= sizeof(short)) return BitConverter.ToInt16(span);
                break;
            case TypeCode.Int32:
                if (span.Length >= sizeof(int)) return BitConverter.ToInt32(span);
                break;
            case TypeCode.Int64:
                if (span.Length >= sizeof(long)) return BitConverter.ToInt64(span);
                break;
            case TypeCode.Single:
                if (span.Length >= sizeof(float)) return BitConverter.ToSingle(span);
                break;
            case TypeCode.Double:
                if (span.Length >= sizeof(double)) return BitConverter.ToDouble(span);
                break;
            default:
                throw new NotSupportedException(
                    $"Conversion to type '{targetType}' is not supported."
                );
        }

        throw new ArgumentException(
            "Span does not contain enough data to convert to the specified type.",
            nameof(span)
        );
    }
}