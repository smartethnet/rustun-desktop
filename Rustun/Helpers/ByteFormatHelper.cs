namespace Rustun.Helpers;

internal static class ByteFormatHelper
{
    private static readonly string[] BinaryUnits = ["B", "KB", "MB", "GB", "TB"];

    /// <summary>以 1024 为步进格式化为可读字符串（如 1.25 MB）。</summary>
    public static string FormatBinary(long bytes)
    {
        if (bytes <= 0)
        {
            return $"0 {BinaryUnits[0]}";
        }

        double value = bytes;
        var unit = 0;
        while (value >= 1024 && unit < BinaryUnits.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return unit == 0
            ? $"{bytes} {BinaryUnits[0]}"
            : $"{value:0.##} {BinaryUnits[unit]}";
    }

    /// <summary>将字节/秒格式化为可读字符串（如 1.25 MB/s），按 1024 进制换单位。</summary>
    public static string FormatBytesPerSecond(double bytesPerSecond)
    {
        if (bytesPerSecond <= 0 || double.IsNaN(bytesPerSecond) || double.IsInfinity(bytesPerSecond))
        {
            return $"0 {BinaryUnits[0]}/s";
        }

        double value = bytesPerSecond;
        var unit = 0;
        while (value >= 1024 && unit < BinaryUnits.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return unit == 0
            ? $"{value:0.#} {BinaryUnits[0]}/s"
            : $"{value:0.##} {BinaryUnits[unit]}/s";
    }
}
