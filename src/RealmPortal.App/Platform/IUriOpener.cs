namespace RealmPortal.App.Platform;

/// <summary>Opens a URI in the system browser (or other registered handler).</summary>
public interface IUriOpener
{
    /// <summary>Returns true when the OS accepted the open request.</summary>
    Task<bool> OpenAsync(Uri uri);
}

/// <summary>Uses <see cref="System.Diagnostics.Process"/> with shell execute (xdg-open / default browser).</summary>
public sealed class ShellUriOpener : IUriOpener
{
    public Task<bool> OpenAsync(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);
        if (uri.Scheme is not ("http" or "https"))
        {
            return Task.FromResult(false);
        }

        try
        {
            var started = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = uri.AbsoluteUri,
                UseShellExecute = true,
            });
            return Task.FromResult(started is not null);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or PlatformNotSupportedException or InvalidOperationException)
        {
            return Task.FromResult(false);
        }
    }
}
