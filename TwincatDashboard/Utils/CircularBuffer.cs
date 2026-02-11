using System.Buffers;

namespace TwincatDashboard.Utils;

/// <summary>
///     Initializes a new instance of the <see cref="CircularBuffer{T}" /> class.
/// </summary>
/// <param name='capacity'>
///     Buffer capacity. Must be positive.
/// </param>
public class CircularBuffer<T>(int capacity) : IDisposable
    where T : struct
{
    private readonly T[] _buffer = ArrayPool.Rent(capacity);

    /// <summary>
    ///     The _end. Index after the last element in the buffer.
    /// </summary>
    private int _end;

    /// <summary>
    ///     The _start. Index of the first element in buffer.
    /// </summary>
    private int _start;

    private static ArrayPool<T> ArrayPool => ArrayPool<T>.Shared;

    public int Capacity => _buffer.Length;
    public bool IsFull => Size == Capacity;
    public bool IsEmpty => Size == 0;
    public int Size => _end >= _start ? _end - _start : Capacity - _end - _start;

    public void Dispose()
    {
        ReturnBufferToArrayPool();
    }

    /// <summary>
    ///     add item to buffer
    /// </summary>
    /// <param name="item"></param>
    public void Add(T item)
    {
        _buffer[_end] = item;
        if (IsFull) _start = ++_start % Capacity;
        _end = ++_end % Capacity;
    }

    public ArraySegment<T> RemoveRange(int size)
    {
        var result = new ArraySegment<T>();
        size = Math.Min(size, Size);
        if (_end >= _start)
        {
            result = new ArraySegment<T>(_buffer, _start, size);
        }
        else
        {
            var result1 = new ArraySegment<T>(_buffer, _start, Capacity - _start);
            var result2 = new ArraySegment<T>(_buffer, 0, size - Capacity + _start);
            result = new ArraySegment<T>(result1.Concat(result2).ToArray());
        }

        _start = (_start + size) % Capacity;
        return result;
    }

    public void ReturnBufferToArrayPool()
    {
        if (_buffer != null) ArrayPool.Return(_buffer);
    }
}