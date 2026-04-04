// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.
//
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued!
//
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.

using MovieG33k.Core.Models;

namespace MovieG33k.Core.Services;

/// <summary>
/// Describes recommendation generation for the local library.
/// </summary>
/// <remarks>
/// The implementation can stay experimental later without forcing the rest of the app to know how scores are produced.
/// </remarks>
public interface IRecommendationService
{
    /// <summary>
    /// Produces recommendation candidates for the supplied media type.
    /// </summary>
    Task<IReadOnlyList<RecommendationCandidate>> GetRecommendationsAsync(
        TitleKind kind,
        string regionCode,
        CancellationToken cancellationToken = default);
}
