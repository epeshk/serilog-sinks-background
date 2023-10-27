namespace BackgroundLogger.Disruptor.Util;

/// <summary>
///   Expose non-inlinable methods to throw exceptions.
/// </summary>
static class ThrowHelper
{
  public static void ThrowArgMustBeGreaterThanZeroAndLessThanBufferSize()
  {
    throw new ArgumentException("n must be > 0 and <= bufferSize");
  }

  public static void ThrowArgumentOutOfRangeException()
  {
    throw new ArgumentOutOfRangeException();
  }
}