namespace AiUsage.Application.Polling;

/// <summary>
/// The poll loop's minimal view of a tile: it only flips loading off and surfaces
/// (or clears) an error message. Implemented by the UI tile view-model.
/// </summary>
public interface IPollStatus
{
    bool IsLoading { set; }
    string ErrorMessage { set; }

    /// <summary>
    /// Valid session, but the provider has no usage data to show (e.g. a ChatGPT
    /// Business/Enterprise plan with no self-serve usage %). Rendered as a muted status
    /// line, not a red error.
    /// </summary>
    void SetUnavailable(string reason);
}
