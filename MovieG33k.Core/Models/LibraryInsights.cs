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
/// Holds the current library analysis for one media type.
/// </summary>
public sealed record LibraryInsights(
    TitleKind Kind,
    int TotalRatedTitles,
    double AverageRatingOutOfFive,
    IReadOnlyList<RatingDistributionBucket> RatingDistribution,
    IReadOnlyList<DecadeRatingBucket> RatingByDecade,
    IReadOnlyList<GenreRatingBucket> RatingByGenre);

/// <summary>
/// Represents one bar in the user's rating distribution.
/// </summary>
public sealed record RatingDistributionBucket(
    int Stars,
    int TitleCount);

/// <summary>
/// Represents the user's average rating for one release decade.
/// </summary>
public sealed record DecadeRatingBucket(
    int DecadeStartYear,
    int TitleCount,
    double AverageRatingOutOfFive);

/// <summary>
/// Represents the user's average rating for one genre.
/// </summary>
public sealed record GenreRatingBucket(
    string Genre,
    int TitleCount,
    double AverageRatingOutOfFive);
