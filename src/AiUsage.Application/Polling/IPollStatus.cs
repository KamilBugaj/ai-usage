namespace AiUsage.Application.Polling;

/// <summary>
/// The poll loop's minimal view of a tile: it only flips loading off and surfaces
/// (or clears) an error message. Implemented by the UI tile view-model.
/// </summary>
public interface IPollStatus
{
    bool IsLoading { set; }
    string ErrorMessage { set; }
}
