namespace AiUsage.Application.Abstractions;

/// <summary>
/// Marshals an action onto the UI thread. Lets orchestration update view-model state
/// without referencing any specific UI framework; the UI layer supplies the implementation.
/// </summary>
public interface IUiDispatcher
{
    void Post(Action action);
}
