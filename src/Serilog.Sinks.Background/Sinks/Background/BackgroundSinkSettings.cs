namespace Serilog.Sinks.Background;

sealed class BackgroundSinkSettings
{
  public int BufferSize { get; set; } = 16 * 1024; // 1024 works fine, enlarged by default for non-blocking mode
  public int WakeBatchSize { get; set; } = 128;
  public int SpinBatchSize { get; set; } = 32;
  public int WakeupMs { get; set; } = Serilog.Sinks.Background.Settings.WakeupMs;
  public bool BlockWhenFull { get; set; } = false;
}