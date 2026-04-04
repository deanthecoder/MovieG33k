// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.
//
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued!
//
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.

using System.Net.Http;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using DTC.Core;
using DTC.Core.UI;
using Material.Icons;
using MovieG33k.Core.Services;
using MovieG33k.Data.Services;
using MovieG33k.Imdb.Services;
using MovieG33k.Settings;
using MovieG33k.Tmdb.Models;
using MovieG33k.Tmdb.Services;
using MovieG33k.ViewModels;

namespace MovieG33k.Views;

/// <summary>
/// Bootstraps the MovieG33k desktop application.
/// </summary>
/// <remarks>
/// The app composition root stays here so the individual projects can remain focused on their own responsibilities.
/// </remarks>
public class App : Application
{
    private HttpClient m_httpClient;

    /// <inheritdoc />
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    /// <inheritdoc />
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            Logger.Instance.SysInfo();
            Logger.Instance.Info("Starting MovieG33k.");

            var settings = MovieG33kSettings.Instance;
            m_httpClient = new HttpClient();
            var tmdbOptions = new TmdbOptions
            {
                AccessToken = GetConfiguredTmdbAccessToken(settings),
                ApiKey = GetConfiguredTmdbApiKey(settings),
                RegionCode = Environment.GetEnvironmentVariable("TMDB_REGION") ?? "GB",
                Language = Environment.GetEnvironmentVariable("TMDB_LANGUAGE") ?? "en-GB"
            };
            Logger.Instance.Info(
                $"TMDb mode: {(string.IsNullOrWhiteSpace(tmdbOptions.AccessToken) ? string.IsNullOrWhiteSpace(tmdbOptions.ApiKey) ? "Not configured" : "API key" : "Access token")} " +
                $"(region {tmdbOptions.RegionCode}, language {tmdbOptions.Language}).");
            var repository = new SqliteLibraryRepository();
            var tmdbClient = new TmdbMetadataClient(m_httpClient, tmdbOptions);
            var discoveryWorkspaceService = new DiscoveryWorkspaceService(repository, tmdbClient);
            var imdbImportService = new ImdbCsvImportService(tmdbClient);

            var viewModel = new MainWindowViewModel(discoveryWorkspaceService, imdbImportService, DialogService.Instance, tmdbOptions.RegionCode);
            var mainWindow = new MainWindow(viewModel);
            desktop.MainWindow = mainWindow;
            mainWindow.Opened += OnMainWindowOpened;
            desktop.Exit += (_, _) =>
            {
                settings.Save();
                m_httpClient.Dispose();
            };

            async void OnMainWindowOpened(object sender, EventArgs args)
            {
                mainWindow.Opened -= OnMainWindowOpened;
                await EnsureTmdbCredentialsAsync(viewModel, tmdbOptions, settings);
            }
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static string GetConfiguredTmdbAccessToken(MovieG33kSettings settings)
    {
        var environmentToken = Environment.GetEnvironmentVariable("TMDB_ACCESS_TOKEN");
        if (!string.IsNullOrWhiteSpace(environmentToken))
            return environmentToken.Trim();

        environmentToken = Environment.GetEnvironmentVariable("TMDB_API_READ_ACCESS_TOKEN");
        if (!string.IsNullOrWhiteSpace(environmentToken))
            return environmentToken.Trim();

        return settings.TmdbAccessToken;
    }

    private static string GetConfiguredTmdbApiKey(MovieG33kSettings settings)
    {
        var environmentKey = Environment.GetEnvironmentVariable("TMDB_API_KEY");
        if (!string.IsNullOrWhiteSpace(environmentKey))
            return environmentKey.Trim();

        return settings.TmdbApiKey;
    }

    private static async Task EnsureTmdbCredentialsAsync(MainWindowViewModel viewModel, TmdbOptions tmdbOptions, MovieG33kSettings settings)
    {
        if (!string.IsNullOrWhiteSpace(tmdbOptions.AccessToken) || !string.IsNullOrWhiteSpace(tmdbOptions.ApiKey))
            return;

        var enteredCredential =
            await DialogService.Instance.ShowTextEntryAsync(
                "Add your TMDb access token",
                "MovieG33k uses TMDb for live search, discovery, and richer metadata. Paste your TMDb API Read Access Token below. If you only have the older API key, that works too. It will be saved on this device so you only need to do this once.",
                watermark: "Paste TMDb access token or API key",
                cancelButton: "Later",
                actionButton: "Save",
                icon: MaterialIconKind.KeyVariant);

        if (string.IsNullOrWhiteSpace(enteredCredential))
            return;

        SaveTmdbCredential(settings, enteredCredential);
        settings.Save();
        tmdbOptions.AccessToken = settings.TmdbAccessToken;
        tmdbOptions.ApiKey = settings.TmdbApiKey;
        Logger.Instance.Info("Saved a TMDb credential from the first-run prompt.");
        await viewModel.RefreshAsync();
    }

    private static void SaveTmdbCredential(MovieG33kSettings settings, string credential)
    {
        var trimmedCredential = credential.Trim();
        var looksLikeAccessToken = trimmedCredential.Count(ch => ch == '.') >= 2;

        if (looksLikeAccessToken)
        {
            settings.TmdbAccessToken = trimmedCredential;
            settings.TmdbApiKey = string.Empty;
            return;
        }

        settings.TmdbApiKey = trimmedCredential;
        settings.TmdbAccessToken = string.Empty;
    }
}
