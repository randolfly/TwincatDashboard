using Serilog;

using System.Buffers;
using System.Runtime.CompilerServices;

namespace TwincatDashboard.Utils;

/// <summary>
///     Initializes a new instance of the <see cref="CircularBuffer{T}" /> class.
/// </summary>
/// <param name='capacity'>
///     Buffer capacity. Must be positive.
/// </param>
public class CircularBuffer<T>(int capacity) : IDisposable where T : struct {
  private T[] _buffer = ArrayPool.Rent(capacity);
  private bool _returned;

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
  public int Size => _end >= _start ? _end - _start : Capacity - _start + _end;

  public void Dispose() {
    ReturnBufferToArrayPool();
  }

  /// <summary>
  ///     add item to buffer
  /// </summary>
  /// <param name="item"></param>
  public void Add(T item) {
    _buffer[_end] = item;
    if (IsFull) _start = ++_start % Capacity;
    _end = ++_end % Capacity;
  }

  public (ReadOnlyMemory<T> First, ReadOnlyMemory<T> Second) RemoveRange(int size) {
    size = Math.Min(size, Size);

    if (size == 0)
      return (default, default);
    if (_end >= _start) {
      // Data is contiguous
      var segment = new ReadOnlyMemory<T>(_buffer, _start, size);
      _start = (_start + size) % Capacity;
      return (segment, default);
    } else {
      // Data is wrapped around the end of the buffer
      int tailCount = Capacity - _start;
      if (size <= tailCount) {
        var segment = new ReadOnlyMemory<T>(_buffer, _start, size);
        _start = (_start + size) % Capacity;
        return (segment, default);
      } else {
        var segment1 = new ReadOnlyMemory<T>(_buffer, _start, tailCount);
        var segment2 = new ReadOnlyMemory<T>(_buffer, 0, size - tailCount);
        _start = (size - tailCount) % Capacity;
        return (segment1, segment2);
      }
    }
  }

  public void ReturnBufferToArrayPool() {
    if (_returned || _buffer.Length == 0)
      return;

    Log.Information("Return CircularBuffer of size: {Size}", Capacity);
    ArrayPool.Return(_buffer, RuntimeHelpers.IsReferenceOrContainsReferences<T>());
    _buffer = Array.Empty<T>();
    _start = 0;
    _end = 0;
    _returned = true;
  }
}