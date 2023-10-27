using Serilog.Configuration;
using Serilog.Sinks.Background;

// ReSharper disable once CheckNamespace
namespace Serilog;

/// <summary>
/// Extends <see cref="LoggerConfiguration"/> with methods for configuring asynchronous logging.
/// </summary>
public static class LoggerConfigurationBackgroundExtensions
{
  /// <summary>
  ///   Configure a sink to be invoked asynchronously, on a background worker thread.
  /// </summary>
  /// <param name="loggerSinkConfiguration">The <see cref="LoggerSinkConfiguration" /> being configured.</param>
  /// <param name="configure">An action that configures the wrapped sink.</param>
  /// <param name="bufferSize">
  ///   The size of the concurrent queue used to feed the background worker thread. If
  ///   the thread is unable to process events quickly enough and the queue is filled, depending on
  ///   <paramref name="blockWhenFull" /> the queue will block or subsequent events will be dropped until
  ///   room is made in the queue.
  /// </param>
  /// <param name="blockWhenFull">Block when the queue is full, instead of dropping events.</param>
  /// <returns>A <see cref="LoggerConfiguration" /> allowing configuration to continue.</returns>
  public static LoggerConfiguration Background(
    this LoggerSinkConfiguration loggerSinkConfiguration,
    Action<LoggerSinkConfiguration> configure,
    int bufferSize = 16 * 1024,
    bool blockWhenFull = false)
  {
#pragma warning disable CS0618 // Type or member is obsolete // Obsolete only in old Serilog versions
    return LoggerSinkConfiguration.Wrap(
      loggerSinkConfiguration,
      wrappedSink => Sink = new BackgroundSink(wrappedSink, new BackgroundSinkSettings
      {
        BufferSize = bufferSize,
        BlockWhenFull = blockWhenFull
      }),
      configure);
#pragma warning restore CS0618 // Type or member is obsolete
  }

  /// <remarks>
  /// Used in benchmarks to access the <see cref="BackgroundSink"/> object.
  /// </remarks>
  internal static volatile BackgroundSink? Sink;
}