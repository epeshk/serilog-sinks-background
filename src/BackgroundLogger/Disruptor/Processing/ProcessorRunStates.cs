namespace BackgroundLogger.Disruptor.Processing;

static class ProcessorRunStates
{
  public const int Idle = 0;
  public const int Running = 1;
  public const int Halted = 2;
}