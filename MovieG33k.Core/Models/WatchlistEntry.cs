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
/// Stores a title the user wants to watch later.
/// </summary>
/// <remarks>
/// Watchlist intent is separate from watched state so the library can express planning as well as history.
/// </remarks>
public sealed record WatchlistEntry(
    TitleIdentifiers Identifiers,
    TitleKind Kind,
    DateTimeOffset AddedUtc,
    int Priority = 0,
    string Notes = null);
