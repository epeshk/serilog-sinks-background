namespace BackgroundLogger.Disruptor.Dsl;

class ExceptionHandlerWrapper<T> : IExceptionHandler<T>
{
  IExceptionHandler<T> _handler = new FatalExceptionHandler<T>();

  public void HandleEventException(Exception ex, long sequence, T evt)
  {
    _handler.HandleEventException(ex, sequence, evt);
  }

  public void HandleOnTimeoutException(Exception ex, long sequence)
  {
    _handler.HandleOnTimeoutException(ex, sequence);
  }

  public void HandleEventException(Exception ex, long sequence, Span<T> batch)
  {
    _handler.HandleEventException(ex, sequence, batch);
  }

  public void HandleOnStartException(Exception ex)
  {
    _handler.HandleOnStartException(ex);
  }

  public void HandleOnShutdownException(Exception ex)
  {
    _handler.HandleOnShutdownException(ex);
  }

  public void SwitchTo(IExceptionHandler<T> exceptionHandler)
  {
    _handler = exceptionHandler;
  }
}