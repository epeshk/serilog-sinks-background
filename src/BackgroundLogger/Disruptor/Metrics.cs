namespace Serilog.Sinks.Background;

static class Metrics
{
    public static readonly ThreadSafeCounter64 EventsDropped = new();
    public static readonly ThreadSafeCounter64 BufferUnavailable = new();
}