#if NET6_0_OR_GREATER
using System.Diagnostics.Tracing;

namespace Serilog.Sinks.Background;

sealed class SerilogBackgroundSinkEventSource : EventSource
{
  public static readonly SerilogBackgroundSinkEventSource Log = new();

  const string EventSourceName = "Serilog.Sinks.Background";

  internal const int CommandStartId = 3;
  internal const int CommandStopId = 4;

  IncrementingPollingCounter? eventsDroppedPerSecondCounter;
  PollingCounter? eventsDroppedCounter;
  IncrementingPollingCounter? bufferUnavailablePerSecondCounter;
  PollingCounter? bufferUnavailableCounter;

  internal SerilogBackgroundSinkEventSource() : base(EventSourceName) {}

  protected override void OnEventCommand(EventCommandEventArgs command)
  {
    try
    {
      if (command.Command == EventCommand.Enable)
      {
        // Comment taken from RuntimeEventSource in CoreCLR
        // NOTE: These counters will NOT be disposed on disable command because we may be introducing
        // a race condition by doing that. We still want to create these lazily so that we aren't adding
        // overhead by at all times even when counters aren't enabled.
        // On disable, PollingCounters will stop polling for values so it should be fine to leave them around.

        eventsDroppedPerSecondCounter = new IncrementingPollingCounter("events-dropped-per-second", this,
          () => Metrics.EventsDropped.Get())
        {
          DisplayName = "Events Dropped",
          DisplayRateTimeScale = TimeSpan.FromSeconds(1)
        };

        eventsDroppedCounter = new PollingCounter("events-dropped", this, () => Metrics.EventsDropped.Get())
        {
          DisplayName = "Events Dropped"
        };

        bufferUnavailablePerSecondCounter = new IncrementingPollingCounter("buffer-unavailable-per-second", this,
          () => Metrics.BufferUnavailable.Get())
        {
          DisplayName = "Buffer Unavailable",
          DisplayRateTimeScale = TimeSpan.FromSeconds(1)
        };

        bufferUnavailableCounter = new PollingCounter("buffer-unavailable", this, () => Metrics.BufferUnavailable.Get())
        {
          DisplayName = "Buffer Unavailable"
        };
      }
    }
    catch (Exception e)
    {
      Console.WriteLine(e);
    }
  }
}
#endif