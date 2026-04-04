// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.
//
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued!
//
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.

using System.IO;
using MovieG33k.Core.Models;
using MovieG33k.Data.Services;

namespace MovieG33k.Tests;

public sealed class SqliteLibraryRepositoryTests
{
    [Test]
    public async Task SearchLibraryAsyncReturnsPersistedTitleWithPersonalState()
    {
        var databaseFile = new FileInfo(Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db"));

        try
        {
            var repository = new SqliteLibraryRepository(databaseFile);
            var title = new MovieEntry(
                new TitleIdentifiers(872585, "tt1517268"),
                "Barbie",
                "Barbie",
                "A doll leaves Barbieland.",
                new DateOnly(2023, 7, 21),
                null,
                null,
                ["Comedy"],
                "en",
                PublicRating: 7.0m);

            await repository.UpsertTitlesAsync([title]);
            await repository.UpsertRatingAsync(new UserRating(title.Identifiers, TitleKind.Movie, 8, DateTimeOffset.UtcNow));
            await repository.UpsertWatchStateAsync(new WatchState(title.Identifiers, TitleKind.Movie, WatchStatus.Watched, DateTimeOffset.UtcNow));
            await repository.UpsertWatchlistEntryAsync(new WatchlistEntry(title.Identifiers, TitleKind.Movie, DateTimeOffset.UtcNow, 2));

            var results = await repository.SearchLibraryAsync("barbie", TitleKind.Movie, 10);

            Assert.That(results, Has.Count.EqualTo(1));
            Assert.That(results[0].Title.Name, Is.EqualTo("Barbie"));
            Assert.That(results[0].Title.PublicRating, Is.EqualTo(7.0m));
            Assert.That(results[0].Rating?.ScoreOutOfTen, Is.EqualTo(8));
            Assert.That(results[0].WatchState?.Status, Is.EqualTo(WatchStatus.Watched));
            Assert.That(results[0].WatchlistEntry, Is.Not.Null);
        }
        finally
        {
            if (databaseFile.Exists)
                databaseFile.Delete();
        }
    }

    [Test]
    public async Task SearchLibraryAsyncPrioritizesExactTitleMatches()
    {
        var databaseFile = new FileInfo(Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db"));

        try
        {
            var repository = new SqliteLibraryRepository(databaseFile);
            await repository.UpsertTitlesAsync(
            [
                new MovieEntry(new TitleIdentifiers(5548, "tt0089886"), "Our RoboCop Remake", "Our RoboCop Remake", "Remake project.", new DateOnly(2014, 2, 11), null, null, ["Documentary"], "en"),
                new MovieEntry(new TitleIdentifiers(5549, "tt0083658"), "Blade Runner", "Blade Runner", "Not a match.", new DateOnly(1982, 6, 25), null, null, ["Science Fiction"], "en"),
                new MovieEntry(new TitleIdentifiers(5550, "tt0093870"), "RoboCop", "RoboCop", "Detroit's future lawman.", new DateOnly(1987, 7, 17), null, null, ["Action"], "en"),
                new MovieEntry(new TitleIdentifiers(5551, "tt0100502"), "RoboCop 2", "RoboCop 2", "The sequel.", new DateOnly(1990, 6, 22), null, null, ["Action"], "en")
            ]);

            var results = await repository.SearchLibraryAsync("Robocop", TitleKind.Movie, 10);

            Assert.That(results, Is.Not.Empty);
            Assert.That(results[0].Title.Name, Is.EqualTo("RoboCop"));
        }
        finally
        {
            if (databaseFile.Exists)
                databaseFile.Delete();
        }
    }

    [Test]
    public async Task GetWatchedAsyncOrdersResultsByRatingDescending()
    {
        var databaseFile = new FileInfo(Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db"));

        try
        {
            var repository = new SqliteLibraryRepository(databaseFile);
            var lowerRated = new MovieEntry(new TitleIdentifiers(1, "tt001"), "Lower Rated", "Lower Rated", "One", new DateOnly(2001, 1, 1), null, null, ["Drama"], "en");
            var higherRated = new MovieEntry(new TitleIdentifiers(2, "tt002"), "Higher Rated", "Higher Rated", "Two", new DateOnly(2002, 1, 1), null, null, ["Drama"], "en");

            await repository.UpsertTitlesAsync([lowerRated, higherRated]);
            await repository.UpsertRatingAsync(new UserRating(lowerRated.Identifiers, TitleKind.Movie, 6, DateTimeOffset.UtcNow.AddDays(-1)));
            await repository.UpsertRatingAsync(new UserRating(higherRated.Identifiers, TitleKind.Movie, 10, DateTimeOffset.UtcNow));
            await repository.UpsertWatchStateAsync(new WatchState(lowerRated.Identifiers, TitleKind.Movie, WatchStatus.Watched, DateTimeOffset.UtcNow.AddDays(-1)));
            await repository.UpsertWatchStateAsync(new WatchState(higherRated.Identifiers, TitleKind.Movie, WatchStatus.Watched, DateTimeOffset.UtcNow));

            var results = await repository.GetWatchedAsync(string.Empty, TitleKind.Movie, 10);

            Assert.That(results.Select(result => result.Title.Name).ToArray(), Is.EqualTo(new[] { "Higher Rated", "Lower Rated" }));
        }
        finally
        {
            if (databaseFile.Exists)
                databaseFile.Delete();
        }
    }

    [Test]
    public async Task GetWatchlistAsyncOrdersResultsByPriorityThenAddedDate()
    {
        var databaseFile = new FileInfo(Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db"));

        try
        {
            var repository = new SqliteLibraryRepository(databaseFile);
            var firstTitle = new MovieEntry(new TitleIdentifiers(1, "tt001"), "First Pick", "First Pick", "One", new DateOnly(2001, 1, 1), null, null, ["Drama"], "en");
            var secondTitle = new MovieEntry(new TitleIdentifiers(2, "tt002"), "Top Pick", "Top Pick", "Two", new DateOnly(2002, 1, 1), null, null, ["Drama"], "en");
            var thirdTitle = new MovieEntry(new TitleIdentifiers(3, "tt003"), "Recent Pick", "Recent Pick", "Three", new DateOnly(2003, 1, 1), null, null, ["Drama"], "en");

            await repository.UpsertTitlesAsync([firstTitle, secondTitle, thirdTitle]);
            await repository.UpsertWatchlistEntryAsync(new WatchlistEntry(firstTitle.Identifiers, TitleKind.Movie, DateTimeOffset.UtcNow.AddDays(-3), 1));
            await repository.UpsertWatchlistEntryAsync(new WatchlistEntry(secondTitle.Identifiers, TitleKind.Movie, DateTimeOffset.UtcNow.AddDays(-10), 3));
            await repository.UpsertWatchlistEntryAsync(new WatchlistEntry(thirdTitle.Identifiers, TitleKind.Movie, DateTimeOffset.UtcNow, 1));

            var results = await repository.GetWatchlistAsync(string.Empty, TitleKind.Movie, 10);

            Assert.That(results.Select(result => result.Title.Name).ToArray(), Is.EqualTo(new[] { "Top Pick", "Recent Pick", "First Pick" }));
        }
        finally
        {
            if (databaseFile.Exists)
                databaseFile.Delete();
        }
    }

    [Test]
    public async Task ResetAsyncClearsPersistedRatingsAndWatchState()
    {
        var databaseFile = new FileInfo(Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db"));

        try
        {
            var repository = new SqliteLibraryRepository(databaseFile);
            var title = new MovieEntry(
                new TitleIdentifiers(872585, "tt1517268"),
                "Barbie",
                "Barbie",
                "A doll leaves Barbieland.",
                new DateOnly(2023, 7, 21),
                null,
                null,
                ["Comedy"],
                "en");

            await repository.UpsertTitlesAsync([title]);
            await repository.UpsertRatingAsync(new UserRating(title.Identifiers, TitleKind.Movie, 8, DateTimeOffset.UtcNow));
            await repository.UpsertWatchStateAsync(new WatchState(title.Identifiers, TitleKind.Movie, WatchStatus.Watched, DateTimeOffset.UtcNow));

            await repository.ResetAsync();

            var watchedResults = await repository.GetWatchedAsync(string.Empty, TitleKind.Movie, 10);
            var libraryResults = await repository.SearchLibraryAsync("barbie", TitleKind.Movie, 10);

            Assert.That(watchedResults, Is.Empty);
            Assert.That(libraryResults, Is.Empty);
        }
        finally
        {
            if (databaseFile.Exists)
                databaseFile.Delete();
        }
    }
}
