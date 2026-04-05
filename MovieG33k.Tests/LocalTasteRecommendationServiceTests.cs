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

public sealed class LocalTasteRecommendationServiceTests
{
    [Test]
    public async Task GetRecommendationsAsyncExcludesWatchedPinnedAndRatedTitles()
    {
        var alien = new MovieEntry(new TitleIdentifiers(348, "tt0078748"), "Alien", "Alien", "One", new DateOnly(1979, 5, 25), null, null, ["Science Fiction", "Horror"], "en", 117, 8.5m);
        var robocop = new MovieEntry(new TitleIdentifiers(5548, "tt0093870"), "RoboCop", "RoboCop", "Two", new DateOnly(1987, 7, 17), null, null, ["Action", "Science Fiction"], "en", 102, 7.4m);
        var hackers = new MovieEntry(new TitleIdentifiers(10428, "tt0113243"), "Hackers", "Hackers", "Three", new DateOnly(1995, 9, 15), null, null, ["Crime", "Thriller"], "en", 107, 6.3m);

        var repository = new FakeLibraryRepository(
            ratedInsights:
            [
                new RatedTitleInsight("movie:1", "Aliens", 10, 1986, ["Science Fiction", "Action"])
            ],
            snapshotsByKey: new Dictionary<string, LibraryItemSnapshot>(StringComparer.OrdinalIgnoreCase)
            {
                [CatalogTitleKey.Create(robocop.Kind, robocop.Identifiers)] = new(robocop, WatchlistEntry: new WatchlistEntry(robocop.Identifiers, TitleKind.Movie, DateTimeOffset.UtcNow)),
                [CatalogTitleKey.Create(hackers.Kind, hackers.Identifiers)] = new(hackers, new UserRating(hackers.Identifiers, TitleKind.Movie, 8, DateTimeOffset.UtcNow))
            });
        var tmdbClient = new FakeTmdbMetadataClient([alien, robocop, hackers]);
        var service = new LocalTasteRecommendationService(repository, tmdbClient);

        var results = await service.GetRecommendationsAsync(new DiscoveryQuery(string.Empty, TitleKind.Movie, "GB", 20));

        Assert.That(results.Select(result => result.Title.Name).ToArray(), Is.EqualTo(new[] { "Alien" }));
    }

    [Test]
    public async Task GetRecommendationsAsyncLetsGenreQueryFilterTheResults()
    {
        var alien = new MovieEntry(new TitleIdentifiers(348, "tt0078748"), "Alien", "Alien", "One", new DateOnly(1979, 5, 25), null, null, ["Science Fiction", "Horror"], "en", 117, 8.5m);
        var robocop = new MovieEntry(new TitleIdentifiers(5548, "tt0093870"), "RoboCop", "RoboCop", "Two", new DateOnly(1987, 7, 17), null, null, ["Action", "Science Fiction"], "en", 102, 7.4m);
        var repository = new FakeLibraryRepository(
            ratedInsights:
            [
                new RatedTitleInsight("movie:1", "Scream", 8, 1996, ["Horror"])
            ]);
        var tmdbClient = new FakeTmdbMetadataClient([alien, robocop]);
        var service = new LocalTasteRecommendationService(repository, tmdbClient);

        var results = await service.GetRecommendationsAsync(new DiscoveryQuery("horror", TitleKind.Movie, "GB", 20));

        Assert.That(results.Select(result => result.Title.Name).ToArray(), Is.EqualTo(new[] { "Alien" }));
    }

    [Test]
    public async Task GetRecommendationsAsyncLetsGenreFilterRestrictTheResults()
    {
        var alien = new MovieEntry(new TitleIdentifiers(348, "tt0078748"), "Alien", "Alien", "One", new DateOnly(1979, 5, 25), null, null, ["Science Fiction", "Horror"], "en", 117, 8.5m);
        var robocop = new MovieEntry(new TitleIdentifiers(5548, "tt0093870"), "RoboCop", "RoboCop", "Two", new DateOnly(1987, 7, 17), null, null, ["Action", "Science Fiction"], "en", 102, 7.4m);
        var repository = new FakeLibraryRepository(
            ratedInsights:
            [
                new RatedTitleInsight("movie:1", "Aliens", 10, 1986, ["Science Fiction", "Action"])
            ]);
        var tmdbClient = new FakeTmdbMetadataClient([alien, robocop]);
        var service = new LocalTasteRecommendationService(repository, tmdbClient);

        var results = await service.GetRecommendationsAsync(new DiscoveryQuery(string.Empty, TitleKind.Movie, "GB", 20, GenreFilter: "Horror"));

        Assert.That(results.Select(result => result.Title.Name).ToArray(), Is.EqualTo(new[] { "Alien" }));
    }

    [Test]
    public async Task GetRecommendationsAsyncLetsAgeRatingRangeRestrictTheResults()
    {
        var alien = new MovieEntry(new TitleIdentifiers(348, "tt0078748"), "Alien", "Alien", "One", new DateOnly(1979, 5, 25), null, null, ["Science Fiction", "Horror"], "en", 117, 8.5m, "18");
        var robocop = new MovieEntry(new TitleIdentifiers(5548, "tt0093870"), "RoboCop", "RoboCop", "Two", new DateOnly(1987, 7, 17), null, null, ["Action", "Science Fiction"], "en", 102, 7.4m, "15");
        var paddington = new MovieEntry(new TitleIdentifiers(116149, "tt1109624"), "Paddington", "Paddington", "Three", new DateOnly(2014, 11, 28), null, null, ["Comedy", "Family"], "en", 95, 7.2m, "PG");
        var repository = new FakeLibraryRepository(
            ratedInsights:
            [
                new RatedTitleInsight("movie:1", "Aliens", 10, 1986, ["Science Fiction", "Action"])
            ]);
        var tmdbClient = new FakeTmdbMetadataClient([alien, robocop, paddington]);
        var service = new LocalTasteRecommendationService(repository, tmdbClient);

        var results = await service.GetRecommendationsAsync(new DiscoveryQuery(string.Empty, TitleKind.Movie, "GB", 20, AgeRatingFilter: "15+"));

        Assert.That(results.Select(result => result.Title.Name).ToArray(), Is.EquivalentTo(new[] { "Alien", "RoboCop" }));
    }

    [Test]
    public async Task GetRecommendationsAsyncUsesLowRatingsAsNegativeTasteSignals()
    {
        var scream = new MovieEntry(new TitleIdentifiers(4232, "tt0117571"), "Scream", "Scream", "One", new DateOnly(1996, 12, 20), null, null, ["Horror"], "en", 111, 9.1m);
        var gattaca = new MovieEntry(new TitleIdentifiers(782, "tt0119177"), "Gattaca", "Gattaca", "Two", new DateOnly(1997, 11, 7), null, null, ["Science Fiction"], "en", 106, 7.7m);
        var repository = new FakeLibraryRepository(
            ratedInsights:
            [
                new RatedTitleInsight("movie:1", "The Thing", 1, 1982, ["Horror"]),
                new RatedTitleInsight("movie:2", "Blade Runner", 10, 1982, ["Science Fiction"])
            ]);
        var tmdbClient = new FakeTmdbMetadataClient([scream, gattaca]);
        var service = new LocalTasteRecommendationService(repository, tmdbClient);

        var results = await service.GetRecommendationsAsync(new DiscoveryQuery(string.Empty, TitleKind.Movie, "GB", 20));

        Assert.That(results.Select(result => result.Title.Name).ToArray(), Is.EqualTo(new[] { "Gattaca" }));
    }

    [Test]
    public async Task GetRecommendationsAsyncBoostsCandidatesFromFavouredDirectors()
    {
        var totalRecall = new MovieEntry(
            new TitleIdentifiers(861, "tt0100802"),
            "Total Recall",
            "Total Recall",
            "One",
            new DateOnly(1990, 6, 1),
            null,
            null,
            ["Action", "Science Fiction"],
            "en",
            113,
            7.5m,
            "18",
            ["Paul Verhoeven"]);
        var speed = new MovieEntry(
            new TitleIdentifiers(1637, "tt0111257"),
            "Speed",
            "Speed",
            "Two",
            new DateOnly(1994, 6, 10),
            null,
            null,
            ["Action"],
            "en",
            116,
            7.3m,
            "15",
            ["Jan de Bont"]);
        var repository = new FakeLibraryRepository(
            ratedInsights:
            [
                new RatedTitleInsight("movie:1", "RoboCop", 10, 1987, ["Action", "Science Fiction"], ["Paul Verhoeven"]),
                new RatedTitleInsight("movie:2", "Starship Troopers", 9, 1997, ["Science Fiction"], ["Paul Verhoeven"])
            ]);
        var tmdbClient = new FakeTmdbMetadataClient([totalRecall, speed]);
        var service = new LocalTasteRecommendationService(repository, tmdbClient);

        var results = await service.GetRecommendationsAsync(new DiscoveryQuery(string.Empty, TitleKind.Movie, "GB", 20));

        Assert.That(results.Select(result => result.Title.Name).ToArray(), Is.EqualTo(new[] { "Total Recall", "Speed" }));
        Assert.That(results[0].Signals, Does.Contain("Paul Verhoeven"));
    }

    [Test]
    public async Task GetRecommendationsAsyncEnrichesCandidateDirectorsBeforeScoring()
    {
        var bareTotalRecall = (CatalogTitle)new MovieEntry(
            new TitleIdentifiers(861, "tt0100802"),
            "Total Recall",
            "Total Recall",
            "One",
            new DateOnly(1990, 6, 1),
            null,
            null,
            ["Action", "Science Fiction"],
            "en",
            113,
            7.5m);
        var detailedTotalRecall = (CatalogTitle)((MovieEntry)bareTotalRecall with
        {
            AgeRating = "18",
            Directors = ["Paul Verhoeven"]
        });
        var repository = new FakeLibraryRepository(
            ratedInsights:
            [
                new RatedTitleInsight("movie:1", "RoboCop", 10, 1987, ["Action", "Science Fiction"], ["Paul Verhoeven"])
            ]);
        var tmdbClient = new FakeTmdbMetadataClient([bareTotalRecall], [detailedTotalRecall]);
        var service = new LocalTasteRecommendationService(repository, tmdbClient);

        var results = await service.GetRecommendationsAsync(new DiscoveryQuery(string.Empty, TitleKind.Movie, "GB", 20));

        Assert.That(results, Has.Count.EqualTo(1));
        Assert.That(results[0].Title.Directors, Does.Contain("Paul Verhoeven"));
        Assert.That(results[0].Signals, Does.Contain("Paul Verhoeven"));
    }

    [Test]
    public async Task GetRecommendationsAsyncEnrichesEnoughAgeRatingsToFillTheFirstPage()
    {
        var discoverTitles = Enumerable.Range(1, 220)
            .Select(index => (CatalogTitle)new MovieEntry(
                new TitleIdentifiers(index, $"tt{index:0000000}"),
                $"Horror {index}",
                $"Horror {index}",
                "One",
                new DateOnly(2000, 1, 1).AddDays(index),
                null,
                null,
                ["Horror"],
                "en",
                100,
                7.0m,
                null))
            .ToArray();
        var detailedTitles = discoverTitles
            .Cast<MovieEntry>()
            .Select(title => (CatalogTitle)(title with { AgeRating = "18" }))
            .ToArray();
        var repository = new FakeLibraryRepository(
            ratedInsights:
            [
                new RatedTitleInsight("movie:1", "Alien", 10, 1979, ["Horror"])
            ]);
        var tmdbClient = new FakeTmdbMetadataClient(discoverTitles, detailedTitles);
        var service = new LocalTasteRecommendationService(repository, tmdbClient);

        var results = await service.GetRecommendationsAsync(
            new DiscoveryQuery(string.Empty, TitleKind.Movie, "GB", 100, GenreFilter: "Horror", AgeRatingFilter: "18+"));

        Assert.That(results, Has.Count.EqualTo(100));
    }

    private sealed class FakeLibraryRepository : ILibraryRepository
    {
        private readonly IReadOnlyList<RatedTitleInsight> m_ratedInsights;
        private readonly Dictionary<string, LibraryItemSnapshot> m_snapshotsByKey;

        public FakeLibraryRepository(
            IReadOnlyList<RatedTitleInsight> ratedInsights,
            IReadOnlyDictionary<string, LibraryItemSnapshot> snapshotsByKey = null)
        {
            m_ratedInsights = ratedInsights;
            m_snapshotsByKey = snapshotsByKey == null
                ? new Dictionary<string, LibraryItemSnapshot>(StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, LibraryItemSnapshot>(snapshotsByKey, StringComparer.OrdinalIgnoreCase);
        }

        public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task UpsertTitlesAsync(IReadOnlyList<CatalogTitle> titles, CancellationToken cancellationToken = default)
        {
            foreach (var title in titles)
            {
                var key = CatalogTitleKey.Create(title.Kind, title.Identifiers);
                m_snapshotsByKey[key] = m_snapshotsByKey.TryGetValue(key, out var existingSnapshot)
                    ? existingSnapshot with { Title = title }
                    : new LibraryItemSnapshot(title);
            }

            return Task.CompletedTask;
        }

        public Task UpsertRatingAsync(UserRating rating, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task UpsertWatchStateAsync(WatchState watchState, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task UpsertWatchlistEntryAsync(WatchlistEntry watchlistEntry, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task DeleteWatchlistEntryAsync(TitleIdentifiers identifiers, TitleKind kind, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task UpsertProviderAvailabilityAsync(TitleIdentifiers identifiers, TitleKind kind, IReadOnlyList<ProviderAvailability> providerAvailabilities, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<IReadOnlyList<LibraryItemSnapshot>> SearchLibraryAsync(string query, TitleKind kind, int maxResults, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<LibraryItemSnapshot>>([]);
        public Task<IReadOnlyDictionary<string, LibraryItemSnapshot>> GetByCatalogKeysAsync(IReadOnlyList<string> catalogKeys, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyDictionary<string, LibraryItemSnapshot>>(
                m_snapshotsByKey
                    .Where(pair => catalogKeys.Contains(pair.Key, StringComparer.OrdinalIgnoreCase))
                    .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase));
        public Task<IReadOnlyList<LibraryItemSnapshot>> GetWatchedAsync(string query, TitleKind kind, int maxResults, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<LibraryItemSnapshot>>([]);
        public Task<IReadOnlyList<LibraryItemSnapshot>> GetWatchlistAsync(string query, TitleKind kind, int maxResults, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<LibraryItemSnapshot>>([]);
        public Task<IReadOnlyList<RatedTitleInsight>> GetRatedTitleInsightsAsync(TitleKind kind, CancellationToken cancellationToken = default) => Task.FromResult(m_ratedInsights);
        public Task<IReadOnlyList<LibraryItemSnapshot>> GetRatedTitlesMissingMetadataAsync(TitleKind kind, int maxResults, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<LibraryItemSnapshot>>([]);
        public Task ResetAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FakeTmdbMetadataClient : ITmdbMetadataClient
    {
        private readonly IReadOnlyList<CatalogTitle> m_results;
        private readonly IReadOnlyDictionary<string, CatalogTitle> m_detailsByKey;

        public FakeTmdbMetadataClient(IReadOnlyList<CatalogTitle> results, IReadOnlyList<CatalogTitle> details = null)
        {
            m_results = results;
            m_detailsByKey = (details ?? results)
                .ToDictionary(
                    result => CatalogTitleKey.Create(result.Kind, result.Identifiers),
                    result => result,
                    StringComparer.OrdinalIgnoreCase);
        }

        public bool IsConfigured => true;
        public string RegionCode => "GB";
        public Task<IReadOnlyList<CatalogTitle>> SearchAsync(DiscoveryQuery query, CancellationToken cancellationToken = default) => Task.FromResult(m_results);
        public Task<IReadOnlyList<CatalogTitle>> GetTrendingAsync(TitleKind kind, int maxResults, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<CatalogTitle>>(m_results.Where(result => result.Kind == kind).Take(maxResults).ToArray());
        public Task<IReadOnlyList<CatalogTitle>> GetDiscoverAsync(DiscoveryQuery query, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<CatalogTitle>>(m_results.Where(result => result.Kind == query.Kind).Take(query.MaxResults).ToArray());
        public Task<CatalogTitle> GetTitleDetailsAsync(TitleIdentifiers identifiers, TitleKind kind, string titleName = null, CancellationToken cancellationToken = default)
        {
            m_detailsByKey.TryGetValue(CatalogTitleKey.Create(kind, identifiers), out var result);
            return Task.FromResult(result);
        }
        public Task<CatalogTitle> ResolveImdbIdAsync(string imdbId, TitleKind kind, CancellationToken cancellationToken = default) => Task.FromResult(m_results.FirstOrDefault());
    }
}
