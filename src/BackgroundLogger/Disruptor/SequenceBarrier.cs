using System.Runtime.CompilerServices;

namespace BackgroundLogger.Disruptor;

/// <summary>
///   Coordination barrier used by event processors for tracking the ring buffer cursor and the sequences of
///   dependent event processors.
/// </summary>
sealed class SequenceBarrier
{
  readonly int minBatchSize;
  CancellationTokenSource cancellationTokenSource = new();
  readonly MultiProducerSequencer sequencer1;
  readonly IWaitStrategy waitStrategy1;
  readonly Sequence dependentSequences1;

  /// <summary>
  ///   Coordination barrier used by event processors for tracking the ring buffer cursor and the sequences of
  ///   dependent event processors.
  /// </summary>
  public SequenceBarrier(MultiProducerSequencer sequencer, IWaitStrategy waitStrategy, Sequence dependentSequences)
  {
    sequencer1 = sequencer;
    waitStrategy1 = waitStrategy;
    dependentSequences1 = dependentSequences;
    minBatchSize = waitStrategy.MinBatchSize;
  }

  public bool IsCancellationRequested => cancellationTokenSource.IsCancellationRequested;

  public CancellationToken CancellationToken
  {
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    get => cancellationTokenSource.Token;
  }

  public void ThrowIfCancellationRequested()
  {
    cancellationTokenSource.Token.ThrowIfCancellationRequested();
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining)]// | Constants.AggressiveOptimization)]
  public long WaitFor(long sequence)
  {
    cancellationTokenSource.Token.ThrowIfCancellationRequested();

    var availableSequence = dependentSequences1.Value;
    if (availableSequence - sequence >= minBatchSize)
    {
      return sequencer1.GetHighestPublishedSequence(sequence, availableSequence);
    }

    return InvokeWaitStrategyAndWaitForPublishedSequence(sequence);
  }

  [MethodImpl(MethodImplOptions.NoInlining)]
  long InvokeWaitStrategy(long sequence)
  {
    return waitStrategy1.WaitFor(sequence, dependentSequences1, cancellationTokenSource.Token);
  }

  [MethodImpl(MethodImplOptions.NoInlining)]
  long InvokeWaitStrategyAndWaitForPublishedSequence(long sequence)
  {
    var waitResult = waitStrategy1.WaitFor(sequence, dependentSequences1, cancellationTokenSource.Token);

    if (waitResult >= sequence)
      return sequencer1.GetHighestPublishedSequence(sequence, waitResult);

    return waitResult;
  }

  public void ResetProcessing()
  {
    // Not disposing the previous value should be fine because the CancellationTokenSource instance
    // has no finalizer and no unmanaged resources to release.

    cancellationTokenSource = new CancellationTokenSource();
  }

  public void CancelProcessing()
  {
    cancellationTokenSource.Cancel();
    waitStrategy1.SignalStopping();
  }
}