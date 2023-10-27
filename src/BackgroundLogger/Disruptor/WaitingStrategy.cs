namespace BackgroundLogger.Disruptor;

sealed class WaitingStrategy : IWaitStrategy
{
  static readonly int maxSpins = 35;

  readonly object sync = new();
  volatile int isWaiting;
  public bool IsBlockingStrategy => true;
  public int MinBatchSize => spinBatchSize1;

  long requestedSequence = 0;
  readonly int wakeBatchSize1;
  readonly int spinBatchSize1;
  int wakeupMs1;

  public WaitingStrategy(int wakeBatchSize, int spinBatchSize, int wakeupMs)
  {
    wakeBatchSize1 = wakeBatchSize;
    spinBatchSize1 = spinBatchSize;
    wakeupMs1 = wakeupMs;
  }


  public long WaitFor(long sequence, Sequence dependentSequences,
    CancellationToken cancellationToken)
  {
    return WaitForInternal(sequence, dependentSequences, cancellationToken);
  }

  long WaitForInternal(long sequence, Sequence dependentSequences,
    CancellationToken cancellationToken)
  {
    if (maxSpins > 0)
    {
      var spinWait = SpinWait(sequence, dependentSequences, cancellationToken);
      if (spinWait != -1)
        return spinWait;
    }

    lock (sync)
    {
      if (dependentSequences.Value < sequence)
      {
        cancellationToken.ThrowIfCancellationRequested();
        Volatile.Write(ref requestedSequence, sequence);
        Interlocked.Exchange(ref isWaiting, 1);
        while (!Monitor.Wait(sync, wakeupMs1) && dependentSequences.Value < sequence)
          cancellationToken.ThrowIfCancellationRequested();
        if (isWaiting == 1)
          isWaiting = 0;
      }
    }

    return dependentSequences.Value;
  }

  long SpinWait(long sequence, Sequence dependentSequences, CancellationToken cancellationToken)
  {
    var spinWait = new AggressiveSpinWait();
    for (int i = 0; i < maxSpins; i++)
    {
      cancellationToken.ThrowIfCancellationRequested();

      var value = dependentSequences.Value;

      if (value - sequence < spinBatchSize1)
      {
        spinWait.SpinOnce();
        continue;
      }

      return value;
    }

    return -1;
  }

  public void SignalAllWhenBlocking()
  {
    if (isWaiting == 1 && Interlocked.CompareExchange(ref isWaiting, 0, 1) == 1)
      lock (sync)
        Monitor.PulseAll(sync);
  }

  public void SignalAllWhenBlocking(long sequence)
  {
    if (isWaiting == 1 && sequence >= Volatile.Read(ref requestedSequence) + wakeBatchSize1 && Interlocked.CompareExchange(ref isWaiting, 0, 1) == 1)
      lock (sync)
        Monitor.PulseAll(sync);
  }

  public void SignalStopping()
  {
    wakeupMs1 = 15;
    SignalAllWhenBlocking();
  }
}