using System.Numerics;

namespace BackgroundLogger.Disruptor;

/// <summary>
///   Set of common functions used by the Disruptor
/// </summary>
static class DisruptorUtil
{
  /// <summary>
  ///   Calculate the log base 2 of the supplied integer, essentially reports the location
  ///   of the highest bit.
  /// </summary>
  /// <param name="i">Value to calculate log2 for.</param>
  /// <returns>The log2 value</returns>
  public static int Log2(int i)
  {
    var r = 0;
    while ((i >>= 1) != 0) ++r;

    return r;
  }

  /// <summary>
  ///   Test whether a given integer is a power of 2
  /// </summary>
  /// <param name="x"></param>
  /// <returns></returns>
  public static bool IsPowerOf2(this int x)
  {
    return x > 0 && (x & (x - 1)) == 0;
  }

  public static uint RoundUpToPowerOf2(uint value)
  {
#if NET6_0_OR_GREATER
    return BitOperations.RoundUpToPowerOf2(value);
#else
    --value;
    value |= value >> 1;
    value |= value >> 2;
    value |= value >> 4;
    value |= value >> 8;
    value |= value >> 16;
    return value + 1;
#endif
  }
}