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
/// Stores the user's viewing progress and completion state for a title.
/// </summary>
/// <remarks>
/// TV shows can later use the season and episode fields without forcing that complexity onto movies.
/// </remarks>
public sealed record WatchState(
    TitleIdentifiers Identifiers,
    TitleKind Kind,
    WatchStatus Status,
    DateTimeOffset? LastWatchedUtc = null,
    int? LastSeasonNumber = null,
    int? LastEpisodeNumber = null);
