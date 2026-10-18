namespace RealmPortal.Core.Launch;

/// <summary>
/// How to start a game client: the executable, an optional command template, extra arguments, environment changes and an
/// optional prefix (a wrapper such as <c>wine</c> or <c>gamemoderun</c>). Nothing here goes through a shell; the result of
/// <see cref="Resolve"/> is a program name plus an argument list.
/// </summary>
/// <param name="ExecutablePath">Full path of the client executable.</param>
/// <param name="WorkingDirectory">Working directory of the process.</param>
/// <param name="CommandTemplate">
/// Optional command line for the executable, e.g. <c>wine {exe}</c>. Split into tokens on whitespace (single and double quotes group
/// words); the placeholders <c>{exe}</c> (required) and <c>{dir}</c> are replaced after splitting, so paths with spaces stay one argument.
/// <c>null</c> means just the executable.
/// </param>
/// <param name="Arguments">Arguments appended after the template (or the executable), one entry per argument.</param>
/// <param name="Environment">Variables to set for the process; a <c>null</c> value removes the variable. <c>null</c> changes nothing.</param>
/// <param name="Prefix">Tokens that go before everything else, e.g. <c>["gamemoderun"]</c> or <c>["env", "A=1"]</c>.</param>
public sealed record GameLaunchSpec(
    string ExecutablePath,
    string WorkingDirectory,
    string? CommandTemplate = null,
    IReadOnlyList<string>? Arguments = null,
    IReadOnlyDictionary<string, string?>? Environment = null,
    IReadOnlyList<string>? Prefix = null)
{
    private static readonly HashSet<string> RestrictedEnvironmentVariables = new(StringComparer.OrdinalIgnoreCase)
    {
        "LD_PRELOAD",
        "LD_LIBRARY_PATH",
        "DYLD_INSERT_LIBRARIES",
    };

    /// <summary><see cref="ArgumentException.ParamName"/> when a launcher cannot apply one of these settings.</summary>
    public const string UnsupportedSettingParameter = "launcherSetting";

    /// <summary>Builds the process to start: <c>Prefix + (Template | executable) + Arguments</c>.</summary>
    /// <exception cref="ArgumentException">Empty path, invalid environment variable name, or a template without <c>{exe}</c> or with an unclosed quote.</exception>
    public ProcessLaunch Resolve()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ExecutablePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(WorkingDirectory);
        if (!Path.IsPathFullyQualified(ExecutablePath) || !File.Exists(ExecutablePath))
            throw new ArgumentException("ExecutablePath must be an existing file specified by an absolute path.", nameof(ExecutablePath));

        var tokens = new List<string>();
        if (Prefix is not null)
            tokens.AddRange(Prefix);

        if (string.IsNullOrWhiteSpace(CommandTemplate))
        {
            tokens.Add(ExecutablePath);
        }
        else
        {
            var words = LaunchCommandTemplate.Split(CommandTemplate);
            if (!words.Any(w => w.Contains("{exe}", StringComparison.Ordinal)))
                throw new ArgumentException("The command template must contain {exe}.", nameof(CommandTemplate));
            tokens.AddRange(words.Select(w => w
                .Replace("{exe}", ExecutablePath, StringComparison.Ordinal)
                .Replace("{dir}", WorkingDirectory, StringComparison.Ordinal)));
        }

        if (Arguments is not null)
            tokens.AddRange(Arguments);

        if (tokens.Count == 0 || string.IsNullOrWhiteSpace(tokens[0]))
            throw new ArgumentException("There is no program to start (empty prefix or template).");

        var environment = new Dictionary<string, string?>(StringComparer.Ordinal);
        if (Environment is not null)
        {
            foreach (var (name, value) in Environment)
            {
                if (string.IsNullOrEmpty(name) || name.Contains('=') || name.Contains('\0'))
                    throw new ArgumentException($"'{name}' is not a valid environment variable name.", nameof(Environment));
                if (RestrictedEnvironmentVariables.Contains(name) && value is not null)
                    throw new ArgumentException($"'{name}' is restricted because it can load arbitrary native code.", UnsupportedSettingParameter);
                environment[name] = value;
            }
        }

        return new ProcessLaunch(tokens[0], tokens.Skip(1).ToList(), WorkingDirectory, environment);
    }
}

/// <summary>A fully resolved process to start.</summary>
/// <param name="FileName">Program to run.</param>
/// <param name="Arguments">Arguments, one entry each (no shell parsing).</param>
/// <param name="WorkingDirectory">Working directory.</param>
/// <param name="Environment">Variables to set; a <c>null</c> value removes the variable.</param>
public sealed record ProcessLaunch(
    string FileName,
    IReadOnlyList<string> Arguments,
    string WorkingDirectory,
    IReadOnlyDictionary<string, string?> Environment);
