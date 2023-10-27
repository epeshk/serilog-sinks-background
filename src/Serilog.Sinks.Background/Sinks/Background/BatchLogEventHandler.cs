using BackgroundLogger.Disruptor;
using Serilog.Core;
using Serilog.Debugging;
using Serilog.Events;

namespace Serilog.Sinks.Background;

sealed class BatchLogEventHandler : IBatchEventHandler<LogEvent>,
  IEventProcessorSequenceAware
{
  Sequence? sequenceCallback;
  readonly ILogEventSink sink1;

  public BatchLogEventHandler(ILogEventSink sink)
  {
    sink1 = sink;
  }

  public void OnBatch(Span<LogEvent> batch, long sequence)
  {
    foreach (var data in batch)
    {
      try
      {
        sink1.Emit(data);
      }
      catch (Exception e)
      {
        if (e is not AggregateException)
          SelfLog.WriteLine(e.ToString());
      }
    }
  }

  public void OnShutdown()
  {
  }

  public void OnStart()
  {
  }

  public void SetSequenceCallback(Sequence sequence)
  {
    this.sequenceCallback = sequence;
  }
}