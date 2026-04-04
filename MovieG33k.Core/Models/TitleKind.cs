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
/// Identifies whether a title is a movie or a TV show.
/// </summary>
/// <remarks>
/// MovieG33k keeps both media types distinct so future discovery, watch-state, and recommendation flows can evolve independently.
/// </remarks>
public enum TitleKind
{
    Movie,
    TvShow
}
