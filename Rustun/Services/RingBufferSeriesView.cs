using System;
using System.Collections;
using System.Collections.Generic;

namespace Rustun.Services;

/// <summary>
/// 将固定长度环形缓冲中的“有效前缀”（按时间从旧到新）暴露为 <see cref="IReadOnlyList{T}"/>，
/// 用于 UI 绑定：避免每次采样都分配新数组。
/// </summary>
internal sealed class RingBufferSeriesView : IReadOnlyList<double>
{
    private readonly double[] _buffer;
    private int _count;

    public RingBufferSeriesView(int capacity)
    {
        if (capacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity));
        }

        _buffer = new double[capacity];
        _count = 0;
    }

    public int Count => _count;

    public double this[int index]
    {
        get
        {
            if ((uint)index >= (uint)_count)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            return _buffer[index];
        }
    }

    public void Reset()
    {
        _count = 0;
    }

    /// <summary>
    /// 将环形缓冲中按时间从旧到新的连续片段写入本视图的前缀（就地覆盖）。
    /// </summary>
    public void CopyFromRing(double[] ring, int ringHead, int ringCount)
    {
        if (ringCount <= 0)
        {
            _count = 0;
            return;
        }

        if (ringCount > ring.Length || ringCount > _buffer.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(ringCount));
        }

        var start = (ringHead - ringCount + ring.Length) % ring.Length;
        var firstPart = Math.Min(ringCount, ring.Length - start);
        Array.Copy(ring, start, _buffer, 0, firstPart);
        if (firstPart < ringCount)
        {
            Array.Copy(ring, 0, _buffer, firstPart, ringCount - firstPart);
        }

        _count = ringCount;
    }

    public IEnumerator<double> GetEnumerator()
    {
        for (var i = 0; i < _count; i++)
        {
            yield return _buffer[i];
        }
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
