using System.Numerics;
using BackgroundLogger.Disruptor;
using BackgroundLogger.Disruptor.Dsl;
using BackgroundLogger.Disruptor.Processing;
using Serilog.Core;
using Serilog.Debugging;
using Serilog.Events;

namespace Serilog.Sinks.Background;

sealed class BackgroundSink : ILogEventSink, IDisposable
{
  readonly RingBuffer<LogEvent> ringBuffer;
  readonly Disruptor<LogEvent> disruptor;
  readonly ILogEventSink sink;
  readonly WaitingStrategy waitStrategy;
  readonly bool blockWhenFull;
  volatile bool isAddingCompleted;

  public BackgroundSink(ILogEventSink sink, BackgroundSinkSettings settings)
  {
#if NET6_0_OR_GREATER
    _ = SerilogBackgroundSinkEventSource.Log;
#endif
    this.sink = sink;
    var bufferSize = settings.BufferSize;
    if (bufferSize < 512)
      throw new ArgumentOutOfRangeException(nameof(settings.BufferSize), bufferSize, "Should be >= 512");
    blockWhenFull = settings.BlockWhenFull;

    waitStrategy = new WaitingStrategy(settings.WakeBatchSize, settings.SpinBatchSize, settings.WakeupMs);

    disruptor = new Disruptor<LogEvent>(
      static () => default!,
      (int)DisruptorUtil.RoundUpToPowerOf2((uint)bufferSize),
      waitStrategy,
      (barrier, consumerSequence, buffer) => 
        new BatchEventProcessor<
          LogEvent,
          EventHandlerWrapper>
        (
          consumerSequence,
          buffer,
          barrier,
          new EventHandlerWrapper(new BatchLogEventHandler(sink)),
          "Serilog_Background")
    );

    ringBuffer = disruptor.Start(Timeout.InfiniteTimeSpan);
  }

  internal void Signal() => waitStrategy.SignalAllWhenBlocking();

  public void Dispose()
  {
    isAddingCompleted = true;

    var shutdownTimeoutMs = Settings.ShutdownTimeoutMs;
    try
    {
      disruptor.Shutdown(TimeSpan.FromMilliseconds(shutdownTimeoutMs));
    }
    catch (TimeoutException e)
    {
      SelfLog.WriteLine(e.ToString());
    }

    try
    {
      (sink as IDisposable)?.Dispose();
    }
    catch (Exception e)
    {
      SelfLog.WriteLine(e.ToString());
    }
  }

  public void Emit(LogEvent logEvent)
  {
    if (isAddingCompleted)
      return;

    try
    {
      // Not now, Thread.Abort under ControlledExecution mask
    }
    finally
    {
      if (blockWhenFull)
        EmitBlockWhenFull(logEvent);
      else
        EmitDropWhenFull(logEvent);
    }
  }

  void EmitDropWhenFull(LogEvent logEvent)
  {
    if (!ringBuffer.TryPublishEvent(logEvent))
      Metrics.EventsDropped.Increment();
  }

  void EmitBlockWhenFull(LogEvent logEvent)
  {
    ringBuffer.PublishEvent(logEvent);
  }
}