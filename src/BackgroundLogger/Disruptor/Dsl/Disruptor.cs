using System.Diagnostics;
using BackgroundLogger.Disruptor.Processing;

namespace BackgroundLogger.Disruptor.Dsl;

/// <summary>
///   A DSL-style API for setting up the disruptor pattern around a ring buffer
///   (aka the Builder pattern).
///   A simple example of setting up the disruptor with two event handlers that
///   must process events in order:
///   <code>var disruptor = new Disruptor{MyEvent}(() => new MyEvent(), 32, TaskScheduler.Default);
/// var handler1 = new EventHandler1() { ... };
/// var handler2 = new EventHandler2() { ... };
/// disruptor.HandleEventsWith(handler1).Then(handler2);
/// 
/// var ringBuffer = disruptor.Start();</code>
/// </summary>
/// <typeparam name="T">the type of event used.</typeparam>
class Disruptor<T>
{
  readonly ExceptionHandlerWrapper<T> _exceptionHandler = new();
  readonly Sequence consumerSequence;
  volatile int _started;
  readonly IEventProcessor consumer;

  /// <summary>
  ///   Create a new Disruptor using <see cref="SequencerFactory.DefaultProducerType" />.
  /// </summary>
  /// <param name="eventFactory">the factory to create events in the ring buffer</param>
  /// <param name="ringBufferSize">the size of the ring buffer, must be power of 2</param>
  /// <param name="waitStrategy">the wait strategy to use for the ring buffer</param>
  public Disruptor(Func<T> eventFactory, int ringBufferSize, IWaitStrategy waitStrategy, Func<SequenceBarrier, Sequence, RingBuffer<T>, IEventProcessor> eventProcessorFactory)
  {
    consumerSequence = new Sequence();
    var sequencer = new MultiProducerSequencer(ringBufferSize, waitStrategy, consumerSequence);
    RingBuffer = new RingBuffer<T>(eventFactory, sequencer);
    var barrier = RingBuffer.NewBarrier();
    consumer = eventProcessorFactory(barrier, consumerSequence, RingBuffer);
  }

  /// <summary>
  ///   The <see cref="RingBuffer{T}" /> used by this disruptor.
  /// </summary>
  public RingBuffer<T> RingBuffer { get; }

  /// <summary>
  ///   Get the value of the cursor indicating the published sequence.
  /// </summary>
  public long Cursor => RingBuffer.Cursor;

  /// <summary>
  ///   The capacity of the data structure to hold entries.
  /// </summary>
  public long BufferSize => RingBuffer.BufferSize;

  /// <summary>
  ///   Get the event for a given sequence in the ring buffer.
  /// </summary>
  /// <param name="sequence">sequence for the event</param>
  /// <returns>event for the sequence</returns>
  public T this[long sequence] => RingBuffer[sequence];

  /// <summary>
  ///   Checks if disruptor has been started.
  /// </summary>
  /// <value>true when start has been called on this instance; otherwise false</value>
  public bool HasStarted => _started == 1;

  /// <summary>
  ///   Specify an exception handler to be used for event handlers and worker pools created by this disruptor.
  ///   The exception handler will be used by existing and future event handlers and worker pools created by this disruptor
  ///   instance.
  /// </summary>
  /// <param name="exceptionHandler">the exception handler to use</param>
  public void SetDefaultExceptionHandler(IExceptionHandler<T> exceptionHandler)
  {
    CheckNotStarted();
    _exceptionHandler.SwitchTo(exceptionHandler);
  }

  /// <summary>
  ///   Starts the event processors and returns the fully configured ring buffer.
  ///   The ring buffer is set up to prevent overwriting any entry that is yet to
  ///   be processed by the slowest event processor.
  ///   This method must only be called once after all event processors have been added.
  /// </summary>
  /// <returns>the configured ring buffer</returns>
  public RingBuffer<T> Start(TimeSpan timeout)
  {
    CheckOnlyStartedOnce();
    consumer.Start();
    consumer.WaitUntilStarted(timeout);
    return RingBuffer;
  }

  /// <summary>
  ///   Calls <see cref="IEventProcessor.Halt" /> on all of the event processors created via this disruptor.
  /// </summary>
  public void Halt()
  {
    consumer.Halt();
  }

  /// <summary>
  ///   Waits until all events currently in the disruptor have been processed by all event processors
  ///   and then halts the processors.It is critical that publishing to the ring buffer has stopped
  ///   before calling this method, otherwise it may never return.
  ///   This method will not await the final termination of the processor threads.
  /// </summary>
  public void Shutdown()
  {
    try
    {
      Shutdown(TimeSpan.FromMilliseconds(-1)); // do not wait
    }
    catch (TimeoutException e)
    {
      _exceptionHandler.HandleOnShutdownException(e);
    }
  }

  /// <summary>
  ///   Waits until all events currently in the disruptor have been processed by all event processors
  ///   and then halts the processors.
  ///   This method will not await the final termination of the processor threads.
  /// </summary>
  /// <param name="timeout">
  ///   the amount of time to wait for all events to be processed. <code>TimeSpan.MaxValue</code> will
  ///   give an infinite timeout
  /// </param>
  /// <exception cref="TimeoutException">if a timeout occurs before shutdown completes.</exception>
  public void Shutdown(TimeSpan timeout)
  {
    var stopwatch = Stopwatch.StartNew();
    var spinWait = new AggressiveSpinWait();
    while (HasBacklog())
    {
      RingBuffer.Signal();
      if (timeout.Ticks >= 0 && stopwatch.Elapsed > timeout)
        throw new TimeoutException();
      else
        spinWait.SpinOnce();
    }

    // Busy spin
    Halt();
  }

  /// <summary>
  ///   Confirms if all messages have been consumed by all event processors.
  /// </summary>
  /// <returns></returns>
  bool HasBacklog() => HasBacklog(RingBuffer.Cursor);

  /// <summary>
  ///   Confirms if all messages have been consumed by all event processors.
  /// </summary>
  /// <returns></returns>
  public bool HasBacklog(long cursor)
  {
    return consumer.IsRunning && cursor > consumer.Sequence.Value;
  }

  void CheckNotStarted()
  {
    if (_started == 1) throw new InvalidOperationException("All event handlers must be added before calling starts.");
  }

  void CheckOnlyStartedOnce()
  {
    if (Interlocked.Exchange(ref _started, 1) != 0)
      throw new InvalidOperationException("Disruptor.start() must only be called once.");
  }

  public override string ToString()
  {
    return $"Disruptor {{RingBuffer={RingBuffer}, Started={_started}}}";
  }
}