namespace BackgroundLogger.Disruptor.Processing;

/// <summary>
///   Convenience class for handling the batching semantics of consuming events from a <see cref="RingBuffer{T}" />
///   and delegating the available events to an <see cref="IBatchEventHandler{T}" />.
/// </summary>
/// <remarks>
///   You should probably not use this type directly but instead implement <see cref="IBatchEventHandler{T}" /> and
///   register your handler
///   using <see cref="Disruptor{T}.HandleEventsWith(IBatchEventHandler{T}[])" />.
/// </remarks>
/// <typeparam name="T">the type of event used.</typeparam>
/// <typeparam name="TDataProvider">the type of the <see cref="IDataProvider{T}" /> used.</typeparam>
/// <typeparam name="TSequenceBarrierOptions">the type of the <see cref="ISequenceBarrierOptions" /> used.</typeparam>
/// <typeparam name="TEventHandler">the type of the <see cref="IBatchEventHandler{T}" /> used.</typeparam>
/// <typeparam name="TBatchSizeLimiter">the type of the <see cref="IBatchSizeLimiter" /> used.</typeparam>
class
  BatchEventProcessor<T, TEventHandler> : IEventProcessor
  where TEventHandler : IBatchEventHandler<T>
{
  // ReSharper disable FieldCanBeMadeReadOnly.Local (performance: the runtime type will be a struct)
  RingBuffer<T> _dataProvider;
  SequenceBarrier _sequenceBarrier;
  TEventHandler _eventHandler;
  readonly string name;

  // ReSharper restore FieldCanBeMadeReadOnly.Local

  readonly ManualResetEventSlim _started = new();
  IExceptionHandler<T> _exceptionHandler = new FatalExceptionHandler<T>();
  volatile int _runState = ProcessorRunStates.Idle;

  public BatchEventProcessor(Sequence consumerSequence, RingBuffer<T> dataProvider, SequenceBarrier sequenceBarrier, TEventHandler eventHandler, string name)
  {
    Sequence = consumerSequence;
    _dataProvider = dataProvider;
    _sequenceBarrier = sequenceBarrier;
    _eventHandler = eventHandler;
    this.name = name;

    if (eventHandler is IEventProcessorSequenceAware sequenceAware)
      sequenceAware.SetSequenceCallback(Sequence);
  }

  /// <inheritdoc />
  public Sequence Sequence { get; }

  /// <inheritdoc />
  public void Halt()
  {
    _runState = ProcessorRunStates.Halted;
    _sequenceBarrier.CancelProcessing();
  }

  /// <inheritdoc />
  public bool IsRunning => _runState != ProcessorRunStates.Idle;

  /// <inheritdoc />
  public void SetExceptionHandler(IExceptionHandler<T> exceptionHandler)
  {
    _exceptionHandler = exceptionHandler ?? throw new ArgumentNullException(nameof(exceptionHandler));
  }

  /// <inheritdoc />
  public void WaitUntilStarted(TimeSpan timeout)
  {
    _started.Wait(timeout);
  }

  /// <inheritdoc />
  public void Start()
  {

    var thread = new Thread(Run)
    {
      Priority = ThreadPriority.Normal,
      Name = name,
      IsBackground = true
    };
    
    thread.Start();
  }

  /// <inheritdoc />
  /// <remarks>
  ///   It is ok to have another thread rerun this method after a halt().
  /// </remarks>
  /// <exception cref="InvalidOperationException">if this object instance is already running in a thread</exception>
  public void Run()
  {
#pragma warning disable 420
    var previousRunning =
      Interlocked.CompareExchange(ref _runState, ProcessorRunStates.Running, ProcessorRunStates.Idle);
#pragma warning restore 420

    if (previousRunning == ProcessorRunStates.Running) throw new InvalidOperationException("Thread is already running");

    if (previousRunning == ProcessorRunStates.Idle)
    {
      _sequenceBarrier.ResetProcessing();

      NotifyStart();
      try
      {
        if (_runState == ProcessorRunStates.Running) ProcessEvents();
      }
      finally
      {
        NotifyShutdown();
        _runState = ProcessorRunStates.Idle;
      }
    }
    else
    {
      EarlyExit();
    }
  }

  void ProcessEvents()
  {
    var nextSequence = Sequence.Value + 1L;
    var availableSequence = Sequence.Value;

    while (true)
      try
      {
        var waitResult = _sequenceBarrier.WaitFor(nextSequence);

        availableSequence = waitResult;
        if (availableSequence >= nextSequence)
        {
          var batch = _dataProvider.GetBatch(nextSequence, availableSequence);
          _eventHandler.OnBatch(batch, nextSequence);
          nextSequence += batch.Length;
        }

        Sequence.SetValue(nextSequence - 1);
      }
      catch (OperationCanceledException) when (_sequenceBarrier.IsCancellationRequested)
      {
        if (_runState != ProcessorRunStates.Running) break;
      }
      catch (Exception ex)
      {
        if (availableSequence >= nextSequence)
        {
          var batch = _dataProvider.GetBatch(nextSequence, availableSequence);
          _exceptionHandler.HandleEventException(ex, nextSequence, batch);
          nextSequence += batch.Length;
        }

        Sequence.SetValue(nextSequence - 1);
      }
  }

  void EarlyExit()
  {
    NotifyStart();
    NotifyShutdown();
  }

  /// <summary>
  ///   Notifies the EventHandler when this processor is starting up
  /// </summary>
  void NotifyStart()
  {
    try
    {
      _eventHandler.OnStart();
    }
    catch (Exception e)
    {
      _exceptionHandler.HandleOnStartException(e);
    }

    _started.Set();
  }

  /// <summary>
  ///   Notifies the EventHandler immediately prior to this processor shutting down
  /// </summary>
  void NotifyShutdown()
  {
    try
    {
      _eventHandler.OnShutdown();
    }
    catch (Exception e)
    {
      _exceptionHandler.HandleOnShutdownException(e);
    }

    _started.Reset();
  }
}