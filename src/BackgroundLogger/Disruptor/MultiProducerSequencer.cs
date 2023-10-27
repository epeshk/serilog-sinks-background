using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using BackgroundLogger.Disruptor.Util;

namespace BackgroundLogger.Disruptor;

/// <summary>
///   <para>
///     Coordinator for claiming sequences for access to a data structure while tracking dependent <see cref="Sequence" />
///     s.
///     Suitable for use for sequencing across multiple publisher threads.
///   </para>
///   <para />
///   <para />
///   Note on <see cref="ICursored.Cursor" />:  With this sequencer the cursor value is updated after the call
///   to <see cref="ISequenced.Next()" />, to determine the highest available sequence that can be read, then
///   <see cref="GetHighestPublishedSequence" /> should be used.
/// </summary>
sealed class MultiProducerSequencer
{
  [SuppressMessage("ReSharper", "PrivateFieldCanBeConvertedToLocalVariable",
    Justification = "Prevents the GC from collecting the array")]
  // availableBuffer tracks the state of each ringbuffer slot
  // see below for more details on the approach:
  // <p>
  // The prime reason is to avoid a shared sequence object between publisher threads.
  // (Keeping single pointers tracking start and end would require coordination
  // between the threads).
  // <p>
  // --  Firstly we have the constraint that the delta between the cursor and minimum
  // gating sequence will never be larger than the buffer size (the code in
  // next/tryNext in the Sequence takes care of that).
  // -- Given that; take the sequence value and mask off the lower portion of the
  // sequence as the index into the buffer (indexMask). (aka modulo operator)
  // -- The upper portion of the sequence becomes the value to check for availability.
  // ie: it tells us how many times around the ring buffer we've been (aka division)
  // -- Because we can't wrap without the gating sequences moving forward (i.e. the
  // minimum gating sequence is effectively our last available position in the
  // buffer), when we have new data and successfully claimed a slot we can simply
  // write over the top.
  readonly int[] _availableBuffer;

  readonly Sequence _cursor = new();

  readonly Sequence _gatingSequenceCache = new();

  readonly int _indexMask;
  readonly int _indexShift;
  readonly bool _isBlockingWaitStrategy;
  readonly IWaitStrategy _waitStrategy;

  // volatile in the Java version => always use Volatile.Read/Write or Interlocked methods to access this field
  Sequence _gatingSequence;


  public MultiProducerSequencer(int bufferSize, IWaitStrategy waitStrategy, Sequence consumerSequence)
  {
    if (bufferSize < 1) throw new ArgumentException("bufferSize must not be less than 1");

    if (!bufferSize.IsPowerOf2()) throw new ArgumentException("bufferSize must be a power of 2");

    BufferSize = bufferSize;
    _waitStrategy = waitStrategy;
    _isBlockingWaitStrategy = waitStrategy.IsBlockingStrategy;
    _availableBuffer = new int[bufferSize];
    _indexMask = bufferSize - 1;
    _indexShift = DisruptorUtil.Log2(bufferSize);

    _availableBuffer.AsSpan().Fill(-1);
    _gatingSequence = consumerSequence;
  }

  /// <inheritdoc />
  public SequenceBarrier NewBarrier()
  {
    return new SequenceBarrier(this, _waitStrategy, _cursor);
  }

  /// <inheritdoc />
  public int BufferSize { get; }

  /// <inheritdoc />
  public long Cursor => _cursor.Value;

  /// <inheritdoc />
  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public bool HasAvailableCapacity(int requiredCapacity)
  {
    return HasAvailableCapacity(requiredCapacity, _cursor.Value);
  }

  /// <inheritdoc />
  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public long Next()
  {
    return NextOneInternal();
  }

  /// <inheritdoc />
  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public long Next(int n)
  {
    if ((uint)(n - 1) >= BufferSize) ThrowHelper.ThrowArgMustBeGreaterThanZeroAndLessThanBufferSize();

    return NextInternal(n);
  }

  /// <inheritdoc />
  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public bool TryNext(out long sequence)
  {
    return TryNextInternal(out sequence);
  }

  public long TryNext()
  {
    while (true)
    {
      var current = _cursor.Value;
      var next = current + 1;

      if (!HasAvailableCapacity(1, current))
        return -1;

      if (_cursor.CompareAndSet(current, next))
        return next;
    }
  }

  /// <inheritdoc />
  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public bool TryNext(int n, out long sequence)
  {
    if (n < 1 || n > BufferSize) ThrowHelper.ThrowArgMustBeGreaterThanZeroAndLessThanBufferSize();

    return TryNextInternal(n, out sequence);
  }

  /// <inheritdoc />
  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public long GetRemainingCapacity()
  {
    var consumed = _gatingSequence.Value;
    var produced = _cursor.Value;
    return BufferSize - (produced - consumed);
  }

  /// <inheritdoc />
  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public void Publish(long sequence)
  {
    SetAvailableBufferValue(CalculateIndex(sequence), CalculateAvailabilityFlag(sequence));

    if (_isBlockingWaitStrategy) _waitStrategy.SignalAllWhenBlocking(sequence);
  }

  /// <inheritdoc />
  public void Publish(long lo, long hi)
  {
    for (var l = lo; l <= hi; l++) SetAvailableBufferValue(CalculateIndex(l), CalculateAvailabilityFlag(l));

    if (_isBlockingWaitStrategy) _waitStrategy.SignalAllWhenBlocking(hi);
  }

  /// <inheritdoc />
  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public bool IsAvailable(long sequence)
  {
    var index = CalculateIndex(sequence);
    var flag = CalculateAvailabilityFlag(sequence);

    return Volatile.Read(ref _availableBuffer[index]) == flag;
  }

  /// <inheritdoc />
  public long GetHighestPublishedSequence(long lowerBound, long availableSequence)
  {
    for (var sequence = lowerBound; sequence <= availableSequence; sequence++)
      if (!IsAvailable(sequence))
        return sequence - 1;

    return availableSequence;
  }

  bool HasAvailableCapacity(int requiredCapacity, long cursorValue)
  {
    var wrapPoint = cursorValue + requiredCapacity - BufferSize;
    var cachedGatingSequence = _gatingSequenceCache.Value;

    if (wrapPoint > cachedGatingSequence || cachedGatingSequence > cursorValue)
    {
      var minSequence = _gatingSequence.Value;
      _gatingSequenceCache.SetValue(minSequence);

      if (wrapPoint > minSequence) return false;
    }

    return true;
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  internal long NextInternal(int n)
  {
    var next = _cursor.AddAndGet(n);
    var current = next - n;
    var wrapPoint = next - BufferSize;
    var cachedGatingSequence = _gatingSequenceCache.Value;

    if (wrapPoint > cachedGatingSequence || cachedGatingSequence > current)
      NextInternalOnWrapPointReached(wrapPoint);

    return next;
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  internal long NextOneInternal()
  {
    var next = _cursor.IncrementAndGet();
    var wrapPoint = next - BufferSize;
    var cachedGatingSequence = _gatingSequenceCache.Value;

    if (wrapPoint > cachedGatingSequence || cachedGatingSequence >= next)
      NextInternalOnWrapPointReached(wrapPoint);

    return next;
  }

  [MethodImpl(MethodImplOptions.NoInlining)]
  void NextInternalOnWrapPointReached(long wrapPoint)
  {
    long minSequence;
    if (wrapPoint > (minSequence = _gatingSequence.Value))
    {
      if (_isBlockingWaitStrategy)
        _waitStrategy.SignalAllWhenBlocking();

      Serilog.Sinks.Background.Metrics.BufferUnavailable.Increment();
      minSequence = WaitForBuffer(wrapPoint);
    }

    _gatingSequenceCache.SetValue(minSequence);
  }

  long WaitForBuffer(long wrapPoint)
  {
    var spinWait = default(AggressiveSpinWait);
    long minSequence;
    while (wrapPoint > (minSequence = _gatingSequence.Value))
      spinWait.SpinOnce();
    return minSequence;
  }

  [MethodImpl(MethodImplOptions.NoInlining)]
  bool TryOnWrapPointReached(long wrapPoint)
  {
    long minSequence;
    if (wrapPoint > (minSequence = _gatingSequence.Value))
    {
      if (_isBlockingWaitStrategy)
        _waitStrategy.SignalAllWhenBlocking();

      return false;
    }

    _gatingSequenceCache.SetValue(minSequence);
    return true;
  }

  readonly ConcurrentQueue<long> cqueue = new();
  volatile int queueCount = 0;
  volatile int queueLock = 0;

  internal bool TryNextInternal(out long sequence)
  {
    if (queueCount > 0)
    {
      sequence = TryRecycleFromQueue();
      if (sequence >= 0)
        return true;
      if (sequence == -1)
        return false;
    }

    var next = _cursor.Increment();
    var current = next - 1;
    var wrapPoint = next - BufferSize;
    var cachedGatingSequence = _gatingSequenceCache.Value;

    sequence = next;
    if (wrapPoint > cachedGatingSequence || cachedGatingSequence > current)
    {
      return TryNextInternalWrap(wrapPoint, next);
    }

    return true;
  }
  internal long TryNext2()
  {
    if (queueCount > 0)
    {
      var sequence = TryRecycleFromQueue();
      if (sequence >= 0)
        return sequence;
      if (sequence == -1)
        return -1;
    }

    var next = _cursor.Increment();
    var wrapPoint = next - BufferSize;
    var cachedGatingSequence = _gatingSequenceCache.Value;

    if (wrapPoint > cachedGatingSequence || cachedGatingSequence >= next)
    {
      return TryNextInternalWrap(wrapPoint, next) ? next : -1;
    }

    return next;
  }

  bool TryNextInternalWrap(long wrapPoint, long next)
  {
    if (TryOnWrapPointReached(wrapPoint))
    {
      return true;
    }

    cqueue.Enqueue(next);
    Interlocked.Increment(ref queueCount);

    return false;
  }


  long TryRecycleFromQueue()
  {
    if (queueLock == 1 || Interlocked.CompareExchange(ref queueLock, 1, 0) != 0)
      return TryNext();

    try
    {
      if (!cqueue.TryDequeue(out var num))
        return -2;

      if (TryOnWrapPointReached(num - BufferSize))
      {
        Interlocked.Decrement(ref queueCount);
        return num;
      }

      cqueue.Enqueue(num);
      return -1;
    }
    finally
    {
      queueLock = 0;
    }
  }

  bool TryNextInternal(int n, out long sequence)
  {
    long current;
    long next;

    do
    {
      current = _cursor.Value;
      next = current + n;

      if (!HasAvailableCapacity(n, current))
      {
        sequence = default;
        return false;
      }
    } while (!_cursor.CompareAndSet(current, next));

    sequence = next;
    return true;
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  void SetAvailableBufferValue(int index, int flag)
  {
    _availableBuffer[index] = flag;
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  int CalculateAvailabilityFlag(long sequence)
  {
    return (int)((ulong)sequence >> _indexShift);
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  int CalculateIndex(long sequence)
  {
    return (int)sequence & _indexMask;
  }

  internal IWaitStrategy GetWaitStrategy()
  {
    return _waitStrategy;
  }
}