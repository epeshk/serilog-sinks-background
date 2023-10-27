# Serilog.Sinks.Background

An asynchronous wrapper for other [Serilog](https://serilog.net) sinks. Use this sink to reduce the overhead of logging calls by delegating work to a background thread. This is especially suited to non-batching sinks that may be affected by I/O bottlenecks.

**Note:** many of the network-based sinks (_CouchDB_, _Elasticsearch_, _MongoDB_, _Seq_, _Splunk_...) already perform asynchronous batching natively and do not benefit from this wrapper.

This sink is designed to be a high-performance replacement for `Serilog.Sinks.Async`. Instead of `BlockingCollection`, this library uses the LMAX Disruptor inter-threaded message passing framework to achieve scalability and high throughput even with millions of events per second. The modified version of [disruptor-net](https://github.com/disruptor-net/Disruptor-net) is embedded in the assembly, so there are no references to external dependencies.

In comparison with `Serilog.Sinks.Async`, this library is:
 - low-latency: adding log events to the queue has minimal impact on the performance of the calling code (when `blockWhenFull` is set to `false`)
 - low-overhead: distributes workload between threads, does not waste time and resources only on synchronization
 - scalable: queue throughput doesn't degrade as the number of threads increases

For better performance, it is recommended to use this library with [RawConsole](https://nuget.org/packages/serilog.sinks.rawconsole) and [RawFile](https://nuget.org/packages/serilog.sinks.rawfile) sinks.

### Getting started

Install from [NuGet](https://nuget.org/packages/serilog.sinks.background):

```powershell
Install-Package Serilog.Sinks.Background
```

Assuming you have already installed the target sink, such as the file sink, move the wrapped sink's configuration within a `WriteTo.Async()` statement:

```csharp
Log.Logger = new LoggerConfiguration()
    .WriteTo.Background(a => a.File("logs/myapp.log"))
    // Other logger configuration
    .CreateLogger()

Log.Information("This will be written to disk on the worker thread");

// At application shutdown (results in monitors getting StopMonitoring calls)
Log.CloseAndFlush();
```

The wrapped sink (`File` in this case) will be invoked on a worker thread while your application's thread gets on with more important stuff.

Because the memory buffer may contain events that have not yet been written to the target sink, it is important to call `Log.CloseAndFlush()` or `Logger.Dispose()` when the application exits.

### Buffering & Dropping

The default memory buffer feeding the worker thread is capped to 16,384 items, after which arriving events will be dropped. Buffer size be a power of 2. To increase or decrease this limit, specify it when configuring the background sink.

```csharp
// Set the buffer to 32768 events
.WriteTo.Background(a => a.File("logs/myapp.log"), bufferSize: 32 * 1024)
```

**Warning:** it is not recommended to decrease the buffer size to avoid loss of log events under high loads. Do not set the buffer size less than 1024.

### Monitoring

This library provides `Serilog.Sinks.Background` event source to monitoring event drops and buffer overflows.

Counters data may be obtained with [dotnet-counters](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/dotnet-counters) tool with following command:

```shell
# monitors Serilog.Sinks.Background event source 
dotnet counters monitor -p PID --counters Serilog.Sinks.Background

# monitors System.Runtime and Serilog.Sinks.Background event sources 
dotnet counters monitor -p PID --counters System.Runtime,Serilog.Sinks.Background
```

### Blocking

Warning: For the same reason one typically does not want exceptions from logging to leak into the execution path, one typically does not want a logger to be able to have the side-effect of actually interrupting application processing until the log propagation has been unblocked.

When the buffer size limit is reached, the default behavior is to drop any further attempted writes until the queue abates, reporting each such failure to the `events-dropped` counter. To replace this with a blocking behaviour, set `blockWhenFull` to `true`.

```csharp
// Wait for any queued event to be accepted by the `File` log before allowing the calling thread to resume its
// application work after a logging call when there are 10,000 LogEvents awaiting ingestion by the pipeline
.WriteTo.Background(a => a.File("logs/myapp.log"), blockWhenFull: true)
```

### JSON configuration

Using [Serilog.Settings.Configuration](https://github.com/serilog/serilog-settings-configuration) JSON:

```json
{
  "Serilog": {
    "WriteTo": [{
      "Name": "Background",
      "Args": {
        "configure": [{
          "Name": "Console"
        }]
      }
    }]
  }
}
```
