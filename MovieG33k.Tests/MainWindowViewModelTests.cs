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
using DTC.Core;
using MovieG33k.Core.Models;
using MovieG33k.Core.Services;
using MovieG33k.Imdb.Services;
using MovieG33k.ViewModels;

namespace MovieG33k.Tests;

public sealed class MainWindowViewModelTests
{
    [Test]
    public async Task RefreshAsyncPreservesSelectedResultWhenItStillExists()
    {
        var titleA = new MovieEntry(
            new TitleIdentifiers(1, "tt001"),
            "Alien",
            "Alien",
            "One",
            new DateOnly(1979, 5, 25),
            null,
            null,
            ["Science Fiction"],
            "en");
        var titleB = new MovieEntry(
            new TitleIdentifiers(2, "tt002"),
            "RoboCop",
            "RoboCop",
            "Two",
            new DateOnly(1987, 7, 17),
            null,
            null,
            ["Action"],
            "en");
        var repository = new FakeLibraryRepository([
            new LibraryItemSnapshot(titleA, SourceLabel: "Local"),
            new LibraryItemSnapshot(titleB, SourceLabel: "Local")
        ]);
        var tmdbClient = new FakeTmdbMetadataClient([]);
        var viewModel = new MainWindowViewModel(
            new DiscoveryWorkspaceService(repository, tmdbClient),
            new ImdbCsvImportService(tmdbClient),
            new FakeDialogService());

        await viewModel.RefreshAsync();
        viewModel.SelectedResult = viewModel.Results[1];

        await viewModel.RefreshAsync();

        Assert.That(viewModel.SelectedResult, Is.Not.Null);
        Assert.That(viewModel.SelectedResult.Title, Is.EqualTo("RoboCop"));
    }

    [Test]
    public async Task LoadMoreAsyncExpandsTheVisibleResultCount()
    {
        var titles = Enumerable.Range(1, 120)
            .Select(index => new LibraryItemSnapshot(
                new MovieEntry(
                    new TitleIdentifiers(index, $"tt{index:0000000}"),
                    $"Movie {index}",
                    $"Movie {index}",
                    $"Overview {index}",
                    new DateOnly(2000, 1, 1).AddDays(index),
                    null,
                    null,
                    ["Drama"],
                    "en"),
                SourceLabel: "Local"))
            .ToArray();
        var repository = new FakeLibraryRepository(titles);
        var tmdbClient = new FakeTmdbMetadataClient([]);
        var viewModel = new MainWindowViewModel(
            new DiscoveryWorkspaceService(repository, tmdbClient),
            new ImdbCsvImportService(tmdbClient),
            new FakeDialogService());

        await viewModel.RefreshAsync();

        Assert.That(viewModel.Results, Has.Count.EqualTo(100));
        Assert.That(viewModel.CanLoadMore, Is.True);

        await viewModel.LoadMoreAsync();

        Assert.That(viewModel.Results, Has.Count.EqualTo(120));
        Assert.That(viewModel.CanLoadMore, Is.False);
    }

    private sealed class FakeLibraryRepository : ILibraryRepository
    {
        private readonly IReadOnlyList<LibraryItemSnapshot> m_searchResults;

        public FakeLibraryRepository(IReadOnlyList<LibraryItemSnapshot> searchResults) => m_searchResults = searchResults;

        public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task UpsertTitlesAsync(IReadOnlyList<CatalogTitle> titles, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task UpsertRatingAsync(UserRating rating, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task UpsertWatchStateAsync(WatchState watchState, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task UpsertWatchlistEntryAsync(WatchlistEntry watchlistEntry, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task DeleteWatchlistEntryAsync(TitleIdentifiers identifiers, TitleKind kind, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task UpsertProviderAvailabilityAsync(TitleIdentifiers identifiers, TitleKind kind, IReadOnlyList<ProviderAvailability> providerAvailabilities, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<IReadOnlyList<LibraryItemSnapshot>> SearchLibraryAsync(string query, TitleKind kind, int maxResults, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<LibraryItemSnapshot>>(m_searchResults.Take(maxResults).ToArray());

        public Task<IReadOnlyDictionary<string, LibraryItemSnapshot>> GetByCatalogKeysAsync(IReadOnlyList<string> catalogKeys, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyDictionary<string, LibraryItemSnapshot>>(
                m_searchResults
                    .Select(snapshot => new KeyValuePair<string, LibraryItemSnapshot>(
                        CatalogTitleKey.Create(snapshot.Title.Kind, snapshot.Title.Identifiers),
                        snapshot))
                    .Where(pair => catalogKeys.Contains(pair.Key, StringComparer.OrdinalIgnoreCase))
                    .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase));

        public Task<IReadOnlyList<LibraryItemSnapshot>> GetWatchedAsync(string query, TitleKind kind, int maxResults, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<LibraryItemSnapshot>>(m_searchResults.Take(maxResults).ToArray());

        public Task<IReadOnlyList<LibraryItemSnapshot>> GetWatchlistAsync(string query, TitleKind kind, int maxResults, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<LibraryItemSnapshot>>(m_searchResults.Take(maxResults).ToArray());

        public Task ResetAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FakeTmdbMetadataClient : ITmdbMetadataClient
    {
        private readonly IReadOnlyList<CatalogTitle> m_results;

        public FakeTmdbMetadataClient(IReadOnlyList<CatalogTitle> results) => m_results = results;

        public bool IsConfigured => true;

        public string RegionCode => "GB";

        public Task<IReadOnlyList<CatalogTitle>> SearchAsync(DiscoveryQuery query, CancellationToken cancellationToken = default) =>
            Task.FromResult(m_results);

        public Task<IReadOnlyList<CatalogTitle>> GetTrendingAsync(TitleKind kind, int maxResults, CancellationToken cancellationToken = default) =>
            Task.FromResult(m_results);

        public Task<CatalogTitle> GetTitleDetailsAsync(TitleIdentifiers identifiers, TitleKind kind, CancellationToken cancellationToken = default) =>
            Task.FromResult(m_results.FirstOrDefault());

        public Task<CatalogTitle> ResolveImdbIdAsync(string imdbId, TitleKind kind, CancellationToken cancellationToken = default) =>
            Task.FromResult(m_results.FirstOrDefault());
    }

    private sealed class FakeDialogService : DTC.Core.UI.IDialogService
    {
        public void Warn(string message, string detail, string cancelButton, string actionButton, Action<bool> onClose, Material.Icons.MaterialIconKind? icon = null) => onClose(false);

        public void ShowMessage(string message, string detail, Material.Icons.MaterialIconKind? icon = null)
        {
        }

        public Task<string> ShowTextEntryAsync(string message, string detail, string initialValue = null, string watermark = null, string cancelButton = "Cancel", string actionButton = "OK", Material.Icons.MaterialIconKind? icon = null) =>
            Task.FromResult<string>(null);

        public Task<FileInfo> ShowFileOpenAsync(string title, string filterName, string[] filterExtensions) =>
            Task.FromResult<FileInfo>(null);

        public Task<FileInfo> ShowFileSaveAsync(string title, string defaultFileName, string filterName, string[] filterExtensions) =>
            Task.FromResult<FileInfo>(null);

        public Task<DirectoryInfo> SelectFolderAsync(string title, DirectoryInfo defaultFolder = null) =>
            Task.FromResult<DirectoryInfo>(null);

        public IDisposable ShowBusy(string message, ProgressToken progress) => null;
    }
}
