// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.
//
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued!
//
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.

using DTC.Core.Settings;

namespace MovieG33k.Settings;

/// <summary>
/// Persists user-specific MovieG33k preferences between sessions.
/// </summary>
/// <remarks>
/// The app stores the user's TMDb credentials locally so first-run setup only needs to happen once per machine.
/// </remarks>
public sealed class MovieG33kSettings : UserSettingsBase
{
    public static MovieG33kSettings Instance { get; } = new();

    protected override string SettingsFileName => "movieg33k-settings.json";

    protected override void ApplyDefaults()
    {
        TmdbAccessToken = string.Empty;
        TmdbApiKey = string.Empty;
    }

    public string TmdbAccessToken
    {
        get => Get<string>() ?? string.Empty;
        set => Set(value?.Trim() ?? string.Empty);
    }

    public string TmdbApiKey
    {
        get => Get<string>() ?? string.Empty;
        set => Set(value?.Trim() ?? string.Empty);
    }
}
