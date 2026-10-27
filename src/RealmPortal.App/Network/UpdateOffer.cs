namespace RealmPortal.App.Network;

/// <summary>
/// A newer GitHub release the user may install. No channel, no IsMandatory.
/// <see cref="ReleaseNotesPlain"/> is plain text ≤20k (no HTML/images/links); <see cref="HtmlUrl"/> is for Open on GitHub only.
/// </summary>
public sealed record UpdateOffer(
    string Version,
    string? TagName,
    string ReleaseNotesPlain,
    string? HtmlUrl)
{
    public const int MaxReleaseNotesChars = 20_000;

    public static string ClampNotes(string? notes)
    {
        if (string.IsNullOrEmpty(notes))
            return string.Empty;
        return notes.Length <= MaxReleaseNotesChars
            ? notes
            : notes[..MaxReleaseNotesChars];
    }
}
