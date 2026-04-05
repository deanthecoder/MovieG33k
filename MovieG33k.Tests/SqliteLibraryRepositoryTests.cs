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
                PublicRating: 7.0m,
                AgeRating: "PG-13",
                Directors: ["Greta Gerwig"]);

            await repository.UpsertTitlesAsync([title]);
            await repository.UpsertRatingAsync(new UserRating(title.Identifiers, TitleKind.Movie, 8, DateTimeOffset.UtcNow));
            await repository.UpsertWatchStateAsync(new WatchState(title.Identifiers, TitleKind.Movie, WatchStatus.Watched, DateTimeOffset.UtcNow));
            await repository.UpsertWatchlistEntryAsync(new WatchlistEntry(title.Identifiers, TitleKind.Movie, DateTimeOffset.UtcNow, 2));

            var results = await repository.SearchLibraryAsync("barbie", TitleKind.Movie, 10);

            Assert.That(results, Has.Count.EqualTo(1));
            Assert.That(results[0].Title.Name, Is.EqualTo("Barbie"));
            Assert.That(results[0].Title.PublicRating, Is.EqualTo(7.0m));
            Assert.That(results[0].Title.AgeRating, Is.EqualTo("PG-13"));
            Assert.That(results[0].Title.Directors, Is.EquivalentTo(new[] { "Greta Gerwig" }));
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
    public async Task SearchLibraryAsyncCanFilterByDirector()
    {
        var databaseFile = new FileInfo(Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db"));

        try
        {
            var repository = new SqliteLibraryRepository(databaseFile);
            await repository.UpsertTitlesAsync(
            [
                new MovieEntry(new TitleIdentifiers(1, "tt001"), "Alien", "Alien", "One", new DateOnly(1979, 5, 25), null, null, ["Science Fiction"], "en", Directors: ["Ridley Scott"]),
                new MovieEntry(new TitleIdentifiers(2, "tt002"), "RoboCop", "RoboCop", "Two", new DateOnly(1987, 7, 17), null, null, ["Action"], "en", Directors: ["Paul Verhoeven"])
            ]);

            var results = await repository.SearchLibraryAsync(string.Empty, TitleKind.Movie, 10, "Ridley Scott");

            Assert.That(results, Has.Count.EqualTo(1));
            Assert.That(results[0].Title.Name, Is.EqualTo("Alien"));
        }
        finally
        {
            if (databaseFile.Exists)
                databaseFile.Delete();
        }
    }

    [Test]
    public async Task SearchLibraryAsyncMatchesDirectorNamesInFreeTextQuery()
    {
        var databaseFile = new FileInfo(Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db"));

        try
        {
            var repository = new SqliteLibraryRepository(databaseFile);
            await repository.UpsertTitlesAsync(
            [
                new MovieEntry(new TitleIdentifiers(1, "tt001"), "Alien", "Alien", "One", new DateOnly(1979, 5, 25), null, null, ["Science Fiction"], "en", Directors: ["Ridley Scott"]),
                new MovieEntry(new TitleIdentifiers(2, "tt002"), "RoboCop", "RoboCop", "Two", new DateOnly(1987, 7, 17), null, null, ["Action"], "en", Directors: ["Paul Verhoeven"])
            ]);

            var results = await repository.SearchLibraryAsync("Ridley", TitleKind.Movie, 10);

            Assert.That(results, Has.Count.EqualTo(1));
            Assert.That(results[0].Title.Name, Is.EqualTo("Alien"));
        }
        finally
        {
            if (databaseFile.Exists)
                databaseFile.Delete();
        }
    }

    [Test]
    public async Task UpsertTitlesAsyncPreservesRicherMovieMetadataWhenLaterSearchRowsAreSparse()
    {
        var databaseFile = new FileInfo(Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db"));

        try
        {
            var repository = new SqliteLibraryRepository(databaseFile);
            var detailedMovie = new MovieEntry(
                new TitleIdentifiers(5548, "tt0093870"),
                "RoboCop",
                "RoboCop",
                "Detroit's future lawman.",
                new DateOnly(1987, 7, 17),
                "/poster.jpg",
                "/backdrop.jpg",
                ["Action", "Science Fiction"],
                "en",
                102,
                7.4m,
                "18",
                ["Paul Verhoeven"]);
            var sparseSearchMovie = new MovieEntry(
                new TitleIdentifiers(5548),
                "RoboCop",
                "RoboCop",
                "Short search summary.",
                new DateOnly(1987, 7, 17),
                "/poster.jpg",
                null,
                ["Action"],
                "en");

            await repository.UpsertTitlesAsync([detailedMovie]);
            await repository.UpsertTitlesAsync([sparseSearchMovie]);

            var results = await repository.SearchLibraryAsync("Paul Verhoeven", TitleKind.Movie, 10);
            var missingMetadata = await repository.GetTitlesMissingMetadataAsync(TitleKind.Movie, 10);

            Assert.That(results, Has.Count.EqualTo(1));
            Assert.That(results[0].Title.Identifiers.ImdbId, Is.EqualTo("tt0093870"));
            Assert.That(results[0].Title.Directors, Is.EquivalentTo(new[] { "Paul Verhoeven" }));
            Assert.That(((MovieEntry)results[0].Title).RuntimeMinutes, Is.EqualTo(102));
            Assert.That(results[0].Title.AgeRating, Is.EqualTo("18"));
            Assert.That(missingMetadata, Is.Empty);
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
    public async Task GetWatchedAsyncMatchesDirectorNamesInFreeTextQuery()
    {
        var databaseFile = new FileInfo(Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db"));

        try
        {
            var repository = new SqliteLibraryRepository(databaseFile);
            var alien = new MovieEntry(new TitleIdentifiers(1, "tt001"), "Alien", "Alien", "One", new DateOnly(1979, 5, 25), null, null, ["Science Fiction"], "en", Directors: ["Ridley Scott"]);
            var robocop = new MovieEntry(new TitleIdentifiers(2, "tt002"), "RoboCop", "RoboCop", "Two", new DateOnly(1987, 7, 17), null, null, ["Action"], "en", Directors: ["Paul Verhoeven"]);

            await repository.UpsertTitlesAsync([alien, robocop]);
            await repository.UpsertWatchStateAsync(new WatchState(alien.Identifiers, TitleKind.Movie, WatchStatus.Watched, DateTimeOffset.UtcNow));
            await repository.UpsertWatchStateAsync(new WatchState(robocop.Identifiers, TitleKind.Movie, WatchStatus.Watched, DateTimeOffset.UtcNow));

            var results = await repository.GetWatchedAsync("Ridley", TitleKind.Movie, 10);

            Assert.That(results, Has.Count.EqualTo(1));
            Assert.That(results[0].Title.Name, Is.EqualTo("Alien"));
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
    public async Task GetWatchlistAsyncMatchesDirectorNamesInFreeTextQuery()
    {
        var databaseFile = new FileInfo(Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db"));

        try
        {
            var repository = new SqliteLibraryRepository(databaseFile);
            var alien = new MovieEntry(new TitleIdentifiers(1, "tt001"), "Alien", "Alien", "One", new DateOnly(1979, 5, 25), null, null, ["Science Fiction"], "en", Directors: ["Ridley Scott"]);
            var robocop = new MovieEntry(new TitleIdentifiers(2, "tt002"), "RoboCop", "RoboCop", "Two", new DateOnly(1987, 7, 17), null, null, ["Action"], "en", Directors: ["Paul Verhoeven"]);

            await repository.UpsertTitlesAsync([alien, robocop]);
            await repository.UpsertWatchlistEntryAsync(new WatchlistEntry(alien.Identifiers, TitleKind.Movie, DateTimeOffset.UtcNow, 1));
            await repository.UpsertWatchlistEntryAsync(new WatchlistEntry(robocop.Identifiers, TitleKind.Movie, DateTimeOffset.UtcNow, 1));

            var results = await repository.GetWatchlistAsync("Ridley", TitleKind.Movie, 10);

            Assert.That(results, Has.Count.EqualTo(1));
            Assert.That(results[0].Title.Name, Is.EqualTo("Alien"));
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

    [Test]
    public async Task GetRatedTitleInsightsAsyncReturnsYearGenreAndRatingData()
    {
        var databaseFile = new FileInfo(Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db"));

        try
        {
            var repository = new SqliteLibraryRepository(databaseFile);
            var robocop = new MovieEntry(
                new TitleIdentifiers(5548, "tt0093870"),
                "RoboCop",
                "RoboCop",
                "Detroit's future lawman.",
                new DateOnly(1987, 7, 17),
                null,
                null,
                ["Action", "Science Fiction"],
                "en",
                Directors: ["Paul Verhoeven"]);
            var hackers = new MovieEntry(
                new TitleIdentifiers(10428, "tt0113243"),
                "Hackers",
                "Hackers",
                "Hack the planet.",
                new DateOnly(1995, 9, 15),
                null,
                null,
                ["Crime", "Thriller"],
                "en");

            await repository.UpsertTitlesAsync([robocop, hackers]);
            await repository.UpsertRatingAsync(new UserRating(robocop.Identifiers, TitleKind.Movie, 10, DateTimeOffset.UtcNow));
            await repository.UpsertRatingAsync(new UserRating(hackers.Identifiers, TitleKind.Movie, 6, DateTimeOffset.UtcNow.AddDays(-1)));

            var results = await repository.GetRatedTitleInsightsAsync(TitleKind.Movie);

            Assert.That(results, Has.Count.EqualTo(2));
            Assert.That(results.Single(item => item.Title == "RoboCop").ReleaseYear, Is.EqualTo(1987));
            Assert.That(results.Single(item => item.Title == "RoboCop").Genres, Is.EquivalentTo(new[] { "Action", "Science Fiction" }));
            Assert.That(results.Single(item => item.Title == "RoboCop").Directors, Is.EquivalentTo(new[] { "Paul Verhoeven" }));
            Assert.That(results.Single(item => item.Title == "Hackers").ScoreOutOfTen, Is.EqualTo(6));
        }
        finally
        {
            if (databaseFile.Exists)
                databaseFile.Delete();
        }
    }

    [Test]
    public async Task GetRatedTitlesMissingMetadataAsyncDoesNotFlagTvShowsJustBecauseDirectorsAreMissing()
    {
        var databaseFile = new FileInfo(Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db"));

        try
        {
            var repository = new SqliteLibraryRepository(databaseFile);
            var breakingBad = new TvShowEntry(
                new TitleIdentifiers(1396, "tt0903747"),
                "Breaking Bad",
                "Breaking Bad",
                "One chemistry teacher.",
                new DateOnly(2008, 1, 20),
                "/poster.jpg",
                null,
                ["Drama"],
                "en",
                5,
                62,
                9.5m,
                "18");

            await repository.UpsertTitlesAsync([breakingBad]);
            await repository.UpsertRatingAsync(new UserRating(breakingBad.Identifiers, TitleKind.TvShow, 10, DateTimeOffset.UtcNow));

            var results = await repository.GetRatedTitlesMissingMetadataAsync(TitleKind.TvShow, 10);

            Assert.That(results, Is.Empty);
        }
        finally
        {
            if (databaseFile.Exists)
                databaseFile.Delete();
        }
    }

    [Test]
    public async Task GetTitlesMissingMetadataAsyncIncludesUnratedMoviesThatStillNeedDetails()
    {
        var databaseFile = new FileInfo(Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db"));

        try
        {
            var repository = new SqliteLibraryRepository(databaseFile);
            var robocop = new MovieEntry(
                new TitleIdentifiers(5548, "tt0093870"),
                "RoboCop",
                "RoboCop",
                "Detroit's future lawman.",
                new DateOnly(1987, 7, 17),
                "/poster.jpg",
                null,
                ["Action"],
                "en");

            await repository.UpsertTitlesAsync([robocop]);

            var results = await repository.GetTitlesMissingMetadataAsync(TitleKind.Movie, 10);

            Assert.That(results, Has.Count.EqualTo(1));
            Assert.That(results[0].Title.Name, Is.EqualTo("RoboCop"));
        }
        finally
        {
            if (databaseFile.Exists)
                databaseFile.Delete();
        }
    }

    [Test]
    public async Task GetTitlesMissingMetadataAsyncDoesNotFlagMoviesUsingUnavailablePlaceholders()
    {
        var databaseFile = new FileInfo(Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db"));

        try
        {
            var repository = new SqliteLibraryRepository(databaseFile);
            var robocop = new MovieEntry(
                new TitleIdentifiers(5548, "tt0093870"),
                "RoboCop",
                "RoboCop",
                "Detroit's future lawman.",
                new DateOnly(1987, 7, 17),
                CatalogTitle.UnknownPosterPath,
                null,
                ["Action"],
                "en",
                CatalogTitle.UnknownRuntimeMinutes,
                null,
                CatalogTitle.UnknownAgeRating,
                [CatalogTitle.UnknownDirector]);

            await repository.UpsertTitlesAsync([robocop]);

            var results = await repository.GetTitlesMissingMetadataAsync(TitleKind.Movie, 10);

            Assert.That(results, Is.Empty);
        }
        finally
        {
            if (databaseFile.Exists)
                databaseFile.Delete();
        }
    }

    [Test]
    public async Task GetTitlesMissingMetadataAsyncDoesNotFlagUpcomingMoviesMissingAgeRatingAndRuntime()
    {
        var databaseFile = new FileInfo(Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db"));

        try
        {
            var repository = new SqliteLibraryRepository(databaseFile);
            var upcomingMovie = new MovieEntry(
                new TitleIdentifiers(1170608, "tt31378509"),
                "Dune: Part Three",
                "Dune: Part Three",
                "One",
                DateOnly.FromDateTime(DateTime.UtcNow).AddDays(30),
                "/poster.jpg",
                null,
                ["Science Fiction"],
                "en",
                null,
                null,
                null,
                ["Denis Villeneuve"]);

            await repository.UpsertTitlesAsync([upcomingMovie]);

            var results = await repository.GetTitlesMissingMetadataAsync(TitleKind.Movie, 10);

            Assert.That(results, Is.Empty);
        }
        finally
        {
            if (databaseFile.Exists)
                databaseFile.Delete();
        }
    }

    [Test]
    public async Task GetTitlesMissingMetadataAsyncDoesNotFlagReleasedMoviesMissingOnlyRuntime()
    {
        var databaseFile = new FileInfo(Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db"));

        try
        {
            var repository = new SqliteLibraryRepository(databaseFile);
            var concertFilm = new MovieEntry(
                new TitleIdentifiers(959935),
                "Hacken Lee Concert Hall",
                "Hacken Lee Concert Hall",
                "One",
                new DateOnly(2008, 4, 3),
                "/poster.jpg",
                null,
                ["Music"],
                "zh",
                null,
                null,
                CatalogTitle.UnknownAgeRating,
                [CatalogTitle.UnknownDirector]);

            await repository.UpsertTitlesAsync([concertFilm]);

            var results = await repository.GetTitlesMissingMetadataAsync(TitleKind.Movie, 10);

            Assert.That(results, Is.Empty);
        }
        finally
        {
            if (databaseFile.Exists)
                databaseFile.Delete();
        }
    }

    [Test]
    public async Task GetRatedTitlesMissingMetadataAsyncDoesNotFlagTvShowsJustBecauseAgeRatingIsMissing()
    {
        var databaseFile = new FileInfo(Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db"));

        try
        {
            var repository = new SqliteLibraryRepository(databaseFile);
            var holbyCity = new TvShowEntry(
                new TitleIdentifiers(1028, "tt0184122"),
                "Holby City",
                "Holby City",
                "Hospital drama.",
                new DateOnly(1999, 1, 12),
                "/poster.jpg",
                null,
                ["Drama"],
                "en",
                23,
                1102,
                6.1m,
                null,
                ["Tony McHale"]);

            await repository.UpsertTitlesAsync([holbyCity]);
            await repository.UpsertRatingAsync(new UserRating(holbyCity.Identifiers, TitleKind.TvShow, 8, DateTimeOffset.UtcNow));

            var results = await repository.GetRatedTitlesMissingMetadataAsync(TitleKind.TvShow, 10);

            Assert.That(results, Is.Empty);
        }
        finally
        {
            if (databaseFile.Exists)
                databaseFile.Delete();
        }
    }

    [Test]
    public async Task GetRatedTitlesMissingMetadataAsyncPrefersStaleMetadataOverRecentlyRefreshedRows()
    {
        var databaseFile = new FileInfo(Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db"));

        try
        {
            var repository = new SqliteLibraryRepository(databaseFile);
            var staleMovie = new MovieEntry(
                new TitleIdentifiers(348, "tt0078748"),
                "Alien",
                "Alien",
                "One",
                new DateOnly(1979, 5, 25),
                "/poster-alien.jpg",
                null,
                ["Science Fiction"],
                "en");
            var refreshedMovie = new MovieEntry(
                new TitleIdentifiers(5548, "tt0093870"),
                "RoboCop",
                "RoboCop",
                "Two",
                new DateOnly(1987, 7, 17),
                "/poster-robocop.jpg",
                null,
                ["Action"],
                "en");

            await repository.UpsertTitlesAsync([staleMovie, refreshedMovie]);
            await repository.UpsertRatingAsync(new UserRating(staleMovie.Identifiers, TitleKind.Movie, 10, DateTimeOffset.UtcNow));
            await repository.UpsertRatingAsync(new UserRating(refreshedMovie.Identifiers, TitleKind.Movie, 8, DateTimeOffset.UtcNow));

            await Task.Delay(25);
            await repository.UpsertTitlesAsync([refreshedMovie with { Directors = ["Paul Verhoeven"], AgeRating = "18", RuntimeMinutes = 102 }]);

            var results = await repository.GetRatedTitlesMissingMetadataAsync(TitleKind.Movie, 10);

            Assert.That(results, Has.Count.EqualTo(1));
            Assert.That(results[0].Title.Name, Is.EqualTo("Alien"));
        }
        finally
        {
            if (databaseFile.Exists)
                databaseFile.Delete();
        }
    }

    [Test]
    public async Task GetRatedTitlesMissingMetadataAsyncDoesNotFlagMoviesWhoseAgeRatingIsMarkedUnknown()
    {
        var databaseFile = new FileInfo(Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db"));

        try
        {
            var repository = new SqliteLibraryRepository(databaseFile);
            var incompleteMovie = new MovieEntry(
                new TitleIdentifiers(214198, "tt0070607"),
                "Rana: The Legend of Shadow Lake",
                "Rana: The Legend of Shadow Lake",
                "One",
                new DateOnly(1981, 1, 1),
                "/poster-rana.jpg",
                null,
                ["Horror"],
                "en",
                89,
                null,
                CatalogTitle.UnknownAgeRating,
                ["Bill Rebane"]);

            await repository.UpsertTitlesAsync([incompleteMovie]);
            await repository.UpsertRatingAsync(new UserRating(incompleteMovie.Identifiers, TitleKind.Movie, 6, DateTimeOffset.UtcNow));

            var results = await repository.GetRatedTitlesMissingMetadataAsync(TitleKind.Movie, 10);

            Assert.That(results, Is.Empty);
        }
        finally
        {
            if (databaseFile.Exists)
                databaseFile.Delete();
        }
    }
}
