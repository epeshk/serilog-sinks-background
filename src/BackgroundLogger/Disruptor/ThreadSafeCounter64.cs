using System.Runtime.InteropServices;

namespace Serilog.Sinks.Background
{
  sealed class ThreadSafeCounter64
  {
    private readonly PaddedLong[] values = new PaddedLong[Environment.ProcessorCount];

    public void Add(long value) => Interlocked.Add(ref this.values[ThreadSafeCounter64.GetIndex()].Value, value);

    public void Increment() => Interlocked.Increment(ref this.values[ThreadSafeCounter64.GetIndex()].Value);

    public long Get()
    {
      PaddedLong[] values = this.values;
      long num = 0;
      for (int index = 0; index < values.Length; ++index)
        num += Interlocked.Read(ref values[index].Value);
      return num;
    }

    public long GetAndReset()
    {
      PaddedLong[] values = this.values;
      long andReset = 0;
      for (int index = 0; index < values.Length; ++index)
        andReset += Interlocked.Exchange(ref values[index].Value, 0L);
      return andReset;
    }

#if NET6_0_OR_GREATER
    static int GetIndex() => Thread.GetCurrentProcessorId() % Environment.ProcessorCount;
#else
    static int GetIndex() => Environment.CurrentManagedThreadId % Environment.ProcessorCount;
#endif

    [StructLayout(LayoutKind.Explicit, Size = 64)]
    struct PaddedLong
    {
      [FieldOffset(64 - sizeof(long))]
      public long Value;
    }
  }
}
