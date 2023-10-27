using System.Runtime.CompilerServices;
using BackgroundLogger.Disruptor;
using Serilog.Events;

namespace Serilog.Sinks.Background;

struct EventHandlerWrapper : IBatchEventHandler<LogEvent>, IEventProcessorSequenceAware
{
  readonly BatchLogEventHandler handler;

  public EventHandlerWrapper(BatchLogEventHandler handler)
  {
    this.handler = handler;
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public void OnBatch(Span<LogEvent> batch, long sequence)
  {
    handler.OnBatch(batch, sequence);
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public void OnShutdown()
  {
    handler.OnShutdown();
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public void OnStart()
  {
    handler.OnStart();
  }

  public void SetSequenceCallback(Sequence sequenceCallback)
  {
    handler.SetSequenceCallback(sequenceCallback);
  }
}