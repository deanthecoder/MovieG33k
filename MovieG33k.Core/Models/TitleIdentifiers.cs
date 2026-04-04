// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.
//
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued!
//
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.

namespace MovieG33k.Core.Models;

/// <summary>
/// Carries the external identifiers used to reconcile titles across providers.
/// </summary>
/// <remarks>
/// TMDb is the primary metadata source, but IMDb identifiers remain useful when importing historical data.
/// </remarks>
public sealed record TitleIdentifiers(int? TmdbId = null, string ImdbId = null);
