using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using BackgroundLogger.Disruptor.Processing;

namespace BackgroundLogger.Disruptor;

/// <summary>
///   Ring based store of reusable entries containing the data representing
///   an event being exchanged between event producer and <see cref="IEventProcessor" />s.
/// </summary>
/// <typeparam name="T">implementation storing the data for sharing during exchange or parallel coordination of an event.</typeparam>
sealed class RingBuffer<T> : RingBufferBase<T>
{
  /// <summary>
  ///   Construct a RingBuffer with the full option set.
  /// </summary>
  /// <param name="eventFactory">eventFactory to create entries for filling the RingBuffer</param>
  /// <param name="sequencer">sequencer to handle the ordering of events moving through the RingBuffer.</param>
  /// <exception cref="ArgumentException">if bufferSize is less than 1 or not a power of 2</exception>
  public RingBuffer(Func<T> eventFactory, MultiProducerSequencer sequencer)
    : base(sequencer, _bufferPadRef)
  {
    Fill(eventFactory);
  }

  internal ReadOnlySpan<T> this[long lo, long hi]
  {
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    get
    {
      var index1 = (int)(lo & _indexMask);
      var index2 = (int)(hi & _indexMask);
      var length = index1 <= index2 ? 1 + index2 - index1 : _bufferSize - index1;

      return _entries.AsSpan(_bufferPadRef + index1, length);
    }
  }

  /// <summary>
  ///   Gets the event for a given sequence in the ring buffer.
  /// </summary>
  /// <param name="sequence">sequence for the event</param>
  /// <remarks>
  ///   This method should be used for publishing events to the ring buffer:
  ///   <code>
  /// long sequence = ringBuffer.Next();
  /// try
  /// {
  ///     var eventToPublish = ringBuffer[sequence];
  ///     // Configure the event
  /// }
  /// finally
  /// {
  ///     ringBuffer.Publish(sequence);
  /// }
  /// </code>
  ///   This method can also be used for event processing but in most cases the processing is performed
  ///   in the provided <see cref="IEventProcessor" /> types or in the event pollers.
  /// </remarks>
  public T this[long sequence]
  {
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    get =>
      _entries[_bufferPadRef + (int)(sequence & _indexMask)];
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    set =>
      _entries[_bufferPadRef + (int)(sequence & _indexMask)] = value;
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public Span<T> GetBatch(long lo, long hi)
  {
    var index1 = (int)(lo & _indexMask);
    var index2 = (int)(hi & _indexMask);
    var length = index1 <= index2 ? 1 + index2 - index1 : _bufferSize - index1;

#if NET6_0_OR_GREATER
    return MemoryMarshal.CreateSpan(
      ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(_entries), _bufferPadRef + index1), length);
#else
    return new Span<T>(_entries, _bufferPadRef + index1, length);
#endif
  }

  void Fill(Func<T> eventFactory)
  {
    var entries = _entries;
    for (var i = 0; i < _bufferSize; i++) entries[_bufferPadRef + i] = eventFactory();
  }

  public override string ToString()
  {
    return
      $"RingBuffer {{Type={typeof(T).Name}, BufferSize={_bufferSize}, Sequencer={_sequencer.GetType().Name}}}";
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public void PublishEvent(T ev)
  {
    var sequence = Next();
    this[sequence] = ev;
    Publish(sequence);
  }

  // [MethodImpl(MethodImplOptions.AggressiveInlining)]
  // public bool TryPublishEvent(T ev)
  // {
  //   var success = TryNext(out var sequence);
  //   if (!success)
  //     return false;
  //   this[sequence] = ev;
  //   Publish(sequence);
  //   return true;
  // }
  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public bool TryPublishEvent(T ev)
  {
    // var sw = new AggressiveSpinWait();
    // for (int i = 0; i < 10; i++)
    // {
    //   var sequence = TryNext();
    //   if (sequence == -1)
    //   {
    //     sw.SpinOnce();
    //     continue;
    //   }
    //   this[sequence] = ev;
    //   Publish(sequence);
    //   return true;
    // }

    // return false;
    //
    // if (!TryNext(out long sequence))
    //   return false;

    var sequence = TryNext();
    if (sequence == -1)
      return false;
    this[sequence] = ev;
    Publish(sequence);
    return true;
  }
}