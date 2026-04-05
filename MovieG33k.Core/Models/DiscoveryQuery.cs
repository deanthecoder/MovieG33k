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
/// Describes a discovery or search request.
/// </summary>
/// <remarks>
/// One query type keeps local search, TMDb search, and future recommendation entry points aligned.
/// </remarks>
public sealed record DiscoveryQuery(
    string Query,
    TitleKind Kind,
    string RegionCode,
    int MaxResults = 20,
    string GenreFilter = null,
    string AgeRatingFilter = null,
    string DirectorFilter = null);
