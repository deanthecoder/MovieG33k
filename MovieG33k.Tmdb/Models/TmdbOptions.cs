// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.
//
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued!
//
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.

namespace MovieG33k.Tmdb.Models;

/// <summary>
/// Holds the configuration needed for TMDb requests.
/// </summary>
/// <remarks>
/// These values are kept in a dedicated type so the client can support both environment-based setup and future persisted settings.
/// </remarks>
public sealed class TmdbOptions
{
    /// <summary>
    /// Gets or sets the TMDb API Read Access Token.
    /// </summary>
    public string AccessToken { get; set; }

    /// <summary>
    /// Gets or sets the TMDb API key.
    /// </summary>
    public string ApiKey { get; set; }

    /// <summary>
    /// Gets or sets the TMDb API base URL.
    /// </summary>
    public string BaseUrl { get; set; } = "https://api.themoviedb.org";

    /// <summary>
    /// Gets or sets the language code for TMDb requests.
    /// </summary>
    public string Language { get; set; } = "en-GB";

    /// <summary>
    /// Gets or sets the region code for TMDb requests.
    /// </summary>
    public string RegionCode { get; set; } = "GB";
}
