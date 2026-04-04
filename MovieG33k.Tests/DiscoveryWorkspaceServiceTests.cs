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
using MovieG33k.Core.Services;

namespace MovieG33k.Tests;

public sealed class DiscoveryWorkspaceServiceTests
{
    [Test]
    public async Task DiscoverAsyncMergesLocalAndRemoteResultsWithoutDuplicates()
    {
        var localTitle = new MovieEntry(
            new TitleIdentifiers(603692, "tt15398776"),
            "Oppenheimer",
            "Oppenheimer",
            "Local cached entry",
            new DateOnly(2023, 7, 21),
            null,
            null,
            ["Drama"],
            "en");
        var remoteTitle = localTitle with { Overview = "Remote TMDb entry" };
        var repository = new FakeLibraryRepository([new LibraryItemSnapshot(localTitle, SourceLabel: "Local")]);
        var tmdbClient = new FakeTmdbMetadataClient([remoteTitle]);
        var service = new DiscoveryWorkspaceService(repository, tmdbClient);

        var result = await service.DiscoverAsync(new DiscoveryQuery("oppenheimer", TitleKind.Movie, "GB", 10));

        Assert.That(result.Items, Has.Count.EqualTo(1));
        Assert.That(result.Items[0].SourceLabel, Is.EqualTo("Search hit"));
        Assert.That(repository.UpsertedTitles, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task DiscoverAsyncUsesTrendingWhenQueryIsEmpty()
    {
        var repository = new FakeLibraryRepository([]);
        var tmdbClient = new FakeTmdbMetadataClient([
            new TvShowEntry(
                new TitleIdentifiers(1396, "tt0903747"),
                "Breaking Bad",
                "Breaking Bad",
                "A teacher goes bad.",
                new DateOnly(2008, 1, 20),
                null,
                null,
                ["Drama"],
                "en")
        ]);
        var service = new DiscoveryWorkspaceService(repository, tmdbClient);

        var result = await service.DiscoverAsync(new DiscoveryQuery(string.Empty, TitleKind.TvShow, "GB", 10));

        Assert.That(result.Items, Has.Count.EqualTo(1));
        Assert.That(result.StatusText, Does.Contain("Popular"));
    }

    [Test]
    public async Task DiscoverAsyncEnrichesRemoteHitsWithStoredRatingWhenLocalQueryDoesNotMatch()
    {
        var title = new MovieEntry(
            new TitleIdentifiers(10681, "tt0089120"),
            "Flight of the Navigator",
            "Flight of the Navigator",
            "A kid disappears and returns years later.",
            new DateOnly(1986, 8, 1),
            null,
            null,
            ["Adventure"],
            "en");
        var repository = new FakeLibraryRepository(
            [],
            snapshotsByKey: new Dictionary<string, LibraryItemSnapshot>(StringComparer.OrdinalIgnoreCase)
            {
                [CatalogTitleKey.Create(title.Kind, title.Identifiers)] = new(
                    title,
                    new UserRating(title.Identifiers, TitleKind.Movie, 8, DateTimeOffset.UtcNow),
                    new WatchState(title.Identifiers, TitleKind.Movie, WatchStatus.Watched, DateTimeOffset.UtcNow),
                    SourceLabel: "Local")
            });
        var tmdbClient = new FakeTmdbMetadataClient([title]);
        var service = new DiscoveryWorkspaceService(repository, tmdbClient);

        var result = await service.DiscoverAsync(new DiscoveryQuery("flight nav", TitleKind.Movie, "GB", 10));

        Assert.That(result.Items, Has.Count.EqualTo(1));
        Assert.That(result.Items[0].Rating?.ScoreOutOfTen, Is.EqualTo(8));
        Assert.That(result.Items[0].SourceLabel, Is.EqualTo("In your library"));
    }

    [Test]
    public async Task GetWatchedAsyncReturnsWatchedItemsStatus()
    {
        var watchedTitle = new MovieEntry(
            new TitleIdentifiers(603692, "tt15398776"),
            "Oppenheimer",
            "Oppenheimer",
            "Watched entry",
            new DateOnly(2023, 7, 21),
            null,
            null,
            ["Drama"],
            "en");
        var repository = new FakeLibraryRepository([], [new LibraryItemSnapshot(
            watchedTitle,
            new UserRating(watchedTitle.Identifiers, TitleKind.Movie, 10, DateTimeOffset.UtcNow),
            new WatchState(watchedTitle.Identifiers, TitleKind.Movie, WatchStatus.Watched, DateTimeOffset.UtcNow),
            SourceLabel: "Local")]);
        var tmdbClient = new FakeTmdbMetadataClient([]);
        var service = new DiscoveryWorkspaceService(repository, tmdbClient);

        var result = await service.GetWatchedAsync(new DiscoveryQuery(string.Empty, TitleKind.Movie, "GB", 10));

        Assert.That(result.Items, Has.Count.EqualTo(1));
        Assert.That(result.StatusText, Does.Contain("ordered by rating"));
    }

    [Test]
    public async Task GetWatchlistAsyncReturnsPinnedItemsStatus()
    {
        var watchlistTitle = new MovieEntry(
            new TitleIdentifiers(603692, "tt15398776"),
            "Oppenheimer",
            "Oppenheimer",
            "Pinned entry",
            new DateOnly(2023, 7, 21),
            null,
            null,
            ["Drama"],
            "en");
        var repository = new FakeLibraryRepository([], [], [new LibraryItemSnapshot(
            watchlistTitle,
            WatchlistEntry: new WatchlistEntry(watchlistTitle.Identifiers, TitleKind.Movie, DateTimeOffset.UtcNow),
            SourceLabel: "Local")]);
        var tmdbClient = new FakeTmdbMetadataClient([]);
        var service = new DiscoveryWorkspaceService(repository, tmdbClient);

        var result = await service.GetWatchlistAsync(new DiscoveryQuery(string.Empty, TitleKind.Movie, "GB", 10));

        Assert.That(result.Items, Has.Count.EqualTo(1));
        Assert.That(result.StatusText, Does.Contain("pinned"));
    }

    [Test]
    public async Task GetTitleDetailsAsyncReturnsRicherMovieMetadata()
    {
        var initialTitle = new MovieEntry(
            new TitleIdentifiers(5548, "tt0093870"),
            "RoboCop",
            "RoboCop",
            "Short summary.",
            new DateOnly(1987, 7, 17),
            "/poster.jpg",
            null,
            ["Action"],
            "en");
        var detailedTitle = initialTitle with
        {
            RuntimeMinutes = 102,
            Overview = "A fuller TMDb summary."
        };
        var repository = new FakeLibraryRepository(
            [],
            snapshotsByKey: new Dictionary<string, LibraryItemSnapshot>(StringComparer.OrdinalIgnoreCase)
            {
                [CatalogTitleKey.Create(detailedTitle.Kind, detailedTitle.Identifiers)] = new(
                    detailedTitle,
                    new UserRating(detailedTitle.Identifiers, TitleKind.Movie, 8, DateTimeOffset.UtcNow),
                    new WatchState(detailedTitle.Identifiers, TitleKind.Movie, WatchStatus.Watched, DateTimeOffset.UtcNow),
                    SourceLabel: "In your library")
            });
        var tmdbClient = new FakeTmdbMetadataClient([], detailsByKey: new Dictionary<string, CatalogTitle>(StringComparer.OrdinalIgnoreCase)
        {
            [CatalogTitleKey.Create(initialTitle.Kind, initialTitle.Identifiers)] = detailedTitle
        });
        var service = new DiscoveryWorkspaceService(repository, tmdbClient);

        var result = await service.GetTitleDetailsAsync(initialTitle);

        Assert.That(result.Title, Is.TypeOf<MovieEntry>());
        Assert.That(((MovieEntry)result.Title).RuntimeMinutes, Is.EqualTo(102));
        Assert.That(result.Rating?.ScoreOutOfTen, Is.EqualTo(8));
    }

    [Test]
    public async Task SaveRatingAsyncRemovesTheTitleFromTheWatchlist()
    {
        var title = new MovieEntry(
            new TitleIdentifiers(5548, "tt0093870"),
            "RoboCop",
            "RoboCop",
            "A cyborg lawman patrols Detroit.",
            new DateOnly(1987, 7, 17),
            null,
            null,
            ["Action"],
            "en",
            102);
        var repository = new FakeLibraryRepository([]);
        var tmdbClient = new FakeTmdbMetadataClient([]);
        var service = new DiscoveryWorkspaceService(repository, tmdbClient);

        await service.SaveRatingAsync(title, 4);

        Assert.That(repository.DeletedWatchlistEntries, Has.Count.EqualTo(1));
        Assert.That(repository.DeletedWatchlistEntries[0], Is.EqualTo((title.Identifiers, TitleKind.Movie)));
    }

    [Test]
    public async Task GetInsightsAsyncBuildsRatingAndGenreBreakdowns()
    {
        var repository = new FakeLibraryRepository(
            [],
            ratedInsights:
            [
                new RatedTitleInsight("movie:1", "RoboCop", 10, 1987, ["Action", "Science Fiction"]),
                new RatedTitleInsight("movie:2", "Aliens", 8, 1986, ["Action", "Science Fiction"]),
                new RatedTitleInsight("movie:3", "Hackers", 6, 1995, ["Crime"])
            ]);
        var tmdbClient = new FakeTmdbMetadataClient([]);
        var service = new DiscoveryWorkspaceService(repository, tmdbClient);

        var insights = await service.GetInsightsAsync(TitleKind.Movie);

        Assert.That(insights.TotalRatedTitles, Is.EqualTo(3));
        Assert.That(insights.AverageRatingOutOfFive, Is.EqualTo(4.0d).Within(0.001d));
        Assert.That(insights.RatingDistribution.Select(bucket => bucket.Stars), Is.EqualTo(new[] { 1, 2, 3, 4, 5 }));
        Assert.That(insights.RatingDistribution.Single(bucket => bucket.Stars == 5).TitleCount, Is.EqualTo(1));
        Assert.That(insights.RatingByDecade.Single(bucket => bucket.DecadeStartYear == 1980).TitleCount, Is.EqualTo(2));
        Assert.That(insights.RatingByGenre.Select(bucket => bucket.Genre).ToArray(), Is.EqualTo(new[] { "Action", "Science Fiction", "Crime" }));
    }

    [Test]
    public async Task GetInsightsAsyncExcludesTitlesWithoutGenresFromGenreBreakdown()
    {
        var repository = new FakeLibraryRepository(
            [],
            ratedInsights:
            [
                new RatedTitleInsight("movie:1", "Unknown", 8, 1999, Array.Empty<string>()),
                new RatedTitleInsight("movie:2", "RoboCop", 10, 1987, ["Action"])
            ]);
        var tmdbClient = new FakeTmdbMetadataClient([]);
        var service = new DiscoveryWorkspaceService(repository, tmdbClient);

        var insights = await service.GetInsightsAsync(TitleKind.Movie);

        Assert.That(insights.RatingByGenre.Select(bucket => bucket.Genre).ToArray(), Is.EqualTo(new[] { "Action" }));
    }

    [Test]
    public async Task GetInsightsAsyncDoesNotCapTheGenreListToEightEntries()
    {
        var repository = new FakeLibraryRepository(
            [],
            ratedInsights:
            [
                new RatedTitleInsight("movie:1", "One", 10, 2001, ["Action"]),
                new RatedTitleInsight("movie:2", "Two", 9, 2002, ["Adventure"]),
                new RatedTitleInsight("movie:3", "Three", 8, 2003, ["Comedy"]),
                new RatedTitleInsight("movie:4", "Four", 7, 2004, ["Crime"]),
                new RatedTitleInsight("movie:5", "Five", 6, 2005, ["Drama"]),
                new RatedTitleInsight("movie:6", "Six", 5, 2006, ["Fantasy"]),
                new RatedTitleInsight("movie:7", "Seven", 4, 2007, ["History"]),
                new RatedTitleInsight("movie:8", "Eight", 3, 2008, ["Horror"]),
                new RatedTitleInsight("movie:9", "Nine", 2, 2009, ["Music"])
            ]);
        var tmdbClient = new FakeTmdbMetadataClient([]);
        var service = new DiscoveryWorkspaceService(repository, tmdbClient);

        var insights = await service.GetInsightsAsync(TitleKind.Movie);

        Assert.That(insights.RatingByGenre, Has.Count.EqualTo(9));
        Assert.That(insights.RatingByGenre.Any(bucket => bucket.Genre == "Comedy"), Is.True);
        Assert.That(insights.RatingByGenre.Any(bucket => bucket.Genre == "Drama"), Is.True);
    }

    private sealed class FakeLibraryRepository : ILibraryRepository
    {
        private readonly IReadOnlyList<LibraryItemSnapshot> m_searchResults;
        private readonly IReadOnlyList<LibraryItemSnapshot> m_watchedResults;
        private readonly IReadOnlyList<LibraryItemSnapshot> m_watchlistResults;
        private readonly IReadOnlyDictionary<string, LibraryItemSnapshot> m_snapshotsByKey;
        private readonly IReadOnlyList<RatedTitleInsight> m_ratedInsights;

        public FakeLibraryRepository(
            IReadOnlyList<LibraryItemSnapshot> searchResults,
            IReadOnlyList<LibraryItemSnapshot> watchedResults = null,
            IReadOnlyList<LibraryItemSnapshot> watchlistResults = null,
            IReadOnlyDictionary<string, LibraryItemSnapshot> snapshotsByKey = null,
            IReadOnlyList<RatedTitleInsight> ratedInsights = null)
        {
            m_searchResults = searchResults;
            m_watchedResults = watchedResults ?? searchResults;
            m_watchlistResults = watchlistResults ?? searchResults;
            m_snapshotsByKey = snapshotsByKey ?? new Dictionary<string, LibraryItemSnapshot>(StringComparer.OrdinalIgnoreCase);
            m_ratedInsights = ratedInsights ?? Array.Empty<RatedTitleInsight>();
        }

        public List<CatalogTitle> UpsertedTitles { get; } = [];
        public List<(TitleIdentifiers Identifiers, TitleKind Kind)> DeletedWatchlistEntries { get; } = [];

        public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task UpsertTitlesAsync(IReadOnlyList<CatalogTitle> titles, CancellationToken cancellationToken = default)
        {
            UpsertedTitles.AddRange(titles);
            return Task.CompletedTask;
        }

        public Task UpsertRatingAsync(UserRating rating, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task UpsertWatchStateAsync(WatchState watchState, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task UpsertWatchlistEntryAsync(WatchlistEntry watchlistEntry, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task DeleteWatchlistEntryAsync(TitleIdentifiers identifiers, TitleKind kind, CancellationToken cancellationToken = default)
        {
            DeletedWatchlistEntries.Add((identifiers, kind));
            return Task.CompletedTask;
        }

        public Task UpsertProviderAvailabilityAsync(TitleIdentifiers identifiers, TitleKind kind, IReadOnlyList<ProviderAvailability> providerAvailabilities, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<IReadOnlyList<LibraryItemSnapshot>> SearchLibraryAsync(string query, TitleKind kind, int maxResults, CancellationToken cancellationToken = default) =>
            Task.FromResult(m_searchResults);

        public Task<IReadOnlyDictionary<string, LibraryItemSnapshot>> GetByCatalogKeysAsync(IReadOnlyList<string> catalogKeys, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyDictionary<string, LibraryItemSnapshot>>(
                catalogKeys
                    .Where(catalogKey => m_snapshotsByKey.ContainsKey(catalogKey))
                    .ToDictionary(catalogKey => catalogKey, catalogKey => m_snapshotsByKey[catalogKey], StringComparer.OrdinalIgnoreCase));

        public Task<IReadOnlyList<LibraryItemSnapshot>> GetWatchedAsync(string query, TitleKind kind, int maxResults, CancellationToken cancellationToken = default) =>
            Task.FromResult(m_watchedResults);

        public Task<IReadOnlyList<LibraryItemSnapshot>> GetWatchlistAsync(string query, TitleKind kind, int maxResults, CancellationToken cancellationToken = default) =>
            Task.FromResult(m_watchlistResults);

        public Task<IReadOnlyList<RatedTitleInsight>> GetRatedTitleInsightsAsync(TitleKind kind, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<RatedTitleInsight>>(m_ratedInsights);

        public Task ResetAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FakeTmdbMetadataClient : ITmdbMetadataClient
    {
        private readonly IReadOnlyList<CatalogTitle> m_results;
        private readonly IReadOnlyDictionary<string, CatalogTitle> m_detailsByKey;

        public FakeTmdbMetadataClient(IReadOnlyList<CatalogTitle> results, IReadOnlyDictionary<string, CatalogTitle> detailsByKey = null)
        {
            m_results = results;
            m_detailsByKey = detailsByKey ?? new Dictionary<string, CatalogTitle>(StringComparer.OrdinalIgnoreCase);
        }

        public bool IsConfigured => true;

        public string RegionCode => "GB";

        public Task<IReadOnlyList<CatalogTitle>> SearchAsync(DiscoveryQuery query, CancellationToken cancellationToken = default) =>
            Task.FromResult(m_results);

        public Task<IReadOnlyList<CatalogTitle>> GetTrendingAsync(TitleKind kind, int maxResults, CancellationToken cancellationToken = default) =>
            Task.FromResult(m_results);

        public Task<IReadOnlyList<CatalogTitle>> GetDiscoverAsync(DiscoveryQuery query, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<CatalogTitle>>(m_results.Where(result => result.Kind == query.Kind).Take(query.MaxResults).ToArray());

        public Task<CatalogTitle> GetTitleDetailsAsync(TitleIdentifiers identifiers, TitleKind kind, CancellationToken cancellationToken = default) =>
            Task.FromResult(
                m_detailsByKey.TryGetValue(CatalogTitleKey.Create(kind, identifiers), out var detailedTitle)
                    ? detailedTitle
                    : m_results.FirstOrDefault());

        public Task<CatalogTitle> ResolveImdbIdAsync(string imdbId, TitleKind kind, CancellationToken cancellationToken = default) =>
            Task.FromResult(m_results.FirstOrDefault());
    }
}
