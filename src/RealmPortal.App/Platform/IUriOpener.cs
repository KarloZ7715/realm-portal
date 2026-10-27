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
    private static readonly TimeSpan HandlerWait = TimeSpan.FromSeconds(5);

    public async Task<bool> OpenAsync(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);
        if (uri.Scheme is not ("http" or "https"))
        {
            return false;
        }

        try
        {
            var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = uri.AbsoluteUri,
                UseShellExecute = true,
            });
            if (process is null)
            {
                return false;
            }

            // Windows UseShellExecute often returns a handle that is not a useful exit code for "browser opened".
            if (OperatingSystem.IsWindows())
            {
                return true;
            }

            // Linux/macOS helpers (xdg-open, open) exit after handing off; non-zero means the handler failed.
            using (process)
            {
                using var cts = new CancellationTokenSource(HandlerWait);
                try
                {
                    await process.WaitForExitAsync(cts.Token).ConfigureAwait(false);
                    return process.ExitCode == 0;
                }
                catch (OperationCanceledException)
                {
                    // Still running after the bound: treat as handed off to a browser.
                    return true;
                }
            }
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or PlatformNotSupportedException or InvalidOperationException)
        {
            return false;
        }
    }
}
