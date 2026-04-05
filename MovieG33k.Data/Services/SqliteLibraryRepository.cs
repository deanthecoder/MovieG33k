// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.
//
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued!
//
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.

using System.Globalization;
using System.IO;
using DTC.Core;
using Microsoft.Data.Sqlite;
using MovieG33k.Core.Models;
using MovieG33k.Core.Services;

namespace MovieG33k.Data.Services;

/// <summary>
/// SQLite-backed repository for cached metadata and user state.
/// </summary>
/// <remarks>
/// This is intentionally pragmatic: one repository owns the initial schema so the rest of the solution can evolve against a stable abstraction.
/// </remarks>
public sealed class SqliteLibraryRepository : ILibraryRepository
{
    private readonly FileInfo m_databaseFile;
    private readonly string m_connectionString;
    private bool m_isInitialized;

    /// <summary>
    /// Creates a repository using the default app-data database path.
    /// </summary>
    public SqliteLibraryRepository()
        : this(new MovieG33kDatabasePathResolver().GetDefaultDatabaseFile())
    {
    }

    /// <summary>
    /// Creates a repository using a specific SQLite file.
    /// </summary>
    public SqliteLibraryRepository(FileInfo databaseFile)
    {
        m_databaseFile = databaseFile ?? throw new ArgumentNullException(nameof(databaseFile));
        m_databaseFile.Directory?.Create();
        m_connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = m_databaseFile.FullName
        }.ToString();
    }

    /// <inheritdoc />
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (m_isInitialized)
            return;

        Logger.Instance.Info($"Initializing MovieG33k SQLite database at '{m_databaseFile.FullName}'.");

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText =
            """
            CREATE TABLE IF NOT EXISTS titles (
                catalog_key TEXT PRIMARY KEY,
                kind TEXT NOT NULL,
                tmdb_id INTEGER NULL,
                imdb_id TEXT NULL,
                name TEXT NOT NULL,
                original_name TEXT NULL,
                overview TEXT NULL,
                release_date TEXT NULL,
                poster_path TEXT NULL,
                backdrop_path TEXT NULL,
                genres TEXT NULL,
                directors TEXT NULL,
                original_language TEXT NULL,
                public_rating REAL NULL,
                age_rating TEXT NULL,
                runtime_minutes INTEGER NULL,
                season_count INTEGER NULL,
                episode_count INTEGER NULL,
                updated_utc TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS user_ratings (
                catalog_key TEXT PRIMARY KEY,
                kind TEXT NOT NULL,
                score_out_of_ten INTEGER NOT NULL,
                notes TEXT NULL,
                updated_utc TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS watch_states (
                catalog_key TEXT PRIMARY KEY,
                kind TEXT NOT NULL,
                status TEXT NOT NULL,
                last_watched_utc TEXT NULL,
                last_season_number INTEGER NULL,
                last_episode_number INTEGER NULL
            );

            CREATE TABLE IF NOT EXISTS watchlist_entries (
                catalog_key TEXT PRIMARY KEY,
                kind TEXT NOT NULL,
                added_utc TEXT NOT NULL,
                priority INTEGER NOT NULL,
                notes TEXT NULL
            );

            CREATE TABLE IF NOT EXISTS provider_availability (
                catalog_key TEXT NOT NULL,
                kind TEXT NOT NULL,
                region_code TEXT NOT NULL,
                provider_id INTEGER NOT NULL,
                provider_name TEXT NOT NULL,
                logo_path TEXT NULL,
                access_model TEXT NOT NULL,
                deep_link TEXT NULL,
                PRIMARY KEY (catalog_key, region_code, provider_id, access_model)
            );

            CREATE TABLE IF NOT EXISTS imdb_import_items (
                imdb_id TEXT PRIMARY KEY,
                kind TEXT NOT NULL,
                title TEXT NOT NULL,
                year INTEGER NULL,
                imdb_rating REAL NULL,
                user_rating INTEGER NULL,
                rated_on TEXT NULL,
                resolved_catalog_key TEXT NULL
            );

            CREATE INDEX IF NOT EXISTS ix_titles_kind_name ON titles(kind, name);
            CREATE INDEX IF NOT EXISTS ix_titles_kind_original_name ON titles(kind, original_name);
            """;

        await command.ExecuteNonQueryAsync(cancellationToken);

        var publicRatingMigrationCommand = connection.CreateCommand();
        publicRatingMigrationCommand.CommandText = "ALTER TABLE titles ADD COLUMN public_rating REAL NULL;";
        try
        {
            await publicRatingMigrationCommand.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 1 && ex.Message.Contains("duplicate column name", StringComparison.OrdinalIgnoreCase))
        {
        }

        var ageRatingMigrationCommand = connection.CreateCommand();
        ageRatingMigrationCommand.CommandText = "ALTER TABLE titles ADD COLUMN age_rating TEXT NULL;";
        try
        {
            await ageRatingMigrationCommand.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 1 && ex.Message.Contains("duplicate column name", StringComparison.OrdinalIgnoreCase))
        {
        }

        var directorsMigrationCommand = connection.CreateCommand();
        directorsMigrationCommand.CommandText = "ALTER TABLE titles ADD COLUMN directors TEXT NULL;";
        try
        {
            await directorsMigrationCommand.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 1 && ex.Message.Contains("duplicate column name", StringComparison.OrdinalIgnoreCase))
        {
        }

        m_isInitialized = true;
        Logger.Instance.Info("MovieG33k SQLite database ready.");
    }

    /// <inheritdoc />
    public async Task UpsertTitlesAsync(IReadOnlyList<CatalogTitle> titles, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(titles);
        await InitializeAsync(cancellationToken);

        var existingSnapshotsByKey =
            titles.Count == 0
                ? new Dictionary<string, LibraryItemSnapshot>(StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, LibraryItemSnapshot>(
                    await GetByCatalogKeysAsync(
                        titles
                            .Select(title => CatalogTitleKey.Create(title.Kind, title.Identifiers))
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .ToArray(),
                        cancellationToken),
                    StringComparer.OrdinalIgnoreCase);

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);

        foreach (var title in titles)
        {
            var catalogKey = CatalogTitleKey.Create(title.Kind, title.Identifiers);
            var mergedTitle =
                existingSnapshotsByKey.TryGetValue(catalogKey, out var existingSnapshot)
                    ? MergeTitle(existingSnapshot.Title, title)
                    : title;
            var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText =
                """
                INSERT INTO titles (
                    catalog_key, kind, tmdb_id, imdb_id, name, original_name, overview,
                    release_date, poster_path, backdrop_path, genres, directors, original_language, public_rating, age_rating,
                    runtime_minutes, season_count, episode_count, updated_utc)
                VALUES (
                    $catalogKey, $kind, $tmdbId, $imdbId, $name, $originalName, $overview,
                    $releaseDate, $posterPath, $backdropPath, $genres, $directors, $originalLanguage, $publicRating, $ageRating,
                    $runtimeMinutes, $seasonCount, $episodeCount, $updatedUtc)
                ON CONFLICT(catalog_key) DO UPDATE SET
                    tmdb_id = excluded.tmdb_id,
                    imdb_id = excluded.imdb_id,
                    name = excluded.name,
                    original_name = excluded.original_name,
                    overview = excluded.overview,
                    release_date = excluded.release_date,
                    poster_path = excluded.poster_path,
                    backdrop_path = excluded.backdrop_path,
                    genres = excluded.genres,
                    directors = excluded.directors,
                    original_language = excluded.original_language,
                    public_rating = excluded.public_rating,
                    age_rating = excluded.age_rating,
                    runtime_minutes = excluded.runtime_minutes,
                    season_count = excluded.season_count,
                    episode_count = excluded.episode_count,
                    updated_utc = excluded.updated_utc;
                """;

            AddTitleParameters(command, mergedTitle);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task UpsertRatingAsync(UserRating rating, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(rating);
        await InitializeAsync(cancellationToken);

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO user_ratings (catalog_key, kind, score_out_of_ten, notes, updated_utc)
            VALUES ($catalogKey, $kind, $score, $notes, $updatedUtc)
            ON CONFLICT(catalog_key) DO UPDATE SET
                score_out_of_ten = excluded.score_out_of_ten,
                notes = excluded.notes,
                updated_utc = excluded.updated_utc;
            """;
        command.Parameters.AddWithValue("$catalogKey", CatalogTitleKey.Create(rating.Kind, rating.Identifiers));
        command.Parameters.AddWithValue("$kind", rating.Kind.ToString());
        command.Parameters.AddWithValue("$score", rating.ScoreOutOfTen);
        command.Parameters.AddWithValue("$notes", (object)rating.Notes ?? DBNull.Value);
        command.Parameters.AddWithValue("$updatedUtc", rating.UpdatedUtc.UtcDateTime.ToString("O", CultureInfo.InvariantCulture));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task UpsertWatchStateAsync(WatchState watchState, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(watchState);
        await InitializeAsync(cancellationToken);

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO watch_states (catalog_key, kind, status, last_watched_utc, last_season_number, last_episode_number)
            VALUES ($catalogKey, $kind, $status, $lastWatchedUtc, $lastSeasonNumber, $lastEpisodeNumber)
            ON CONFLICT(catalog_key) DO UPDATE SET
                status = excluded.status,
                last_watched_utc = excluded.last_watched_utc,
                last_season_number = excluded.last_season_number,
                last_episode_number = excluded.last_episode_number;
            """;
        command.Parameters.AddWithValue("$catalogKey", CatalogTitleKey.Create(watchState.Kind, watchState.Identifiers));
        command.Parameters.AddWithValue("$kind", watchState.Kind.ToString());
        command.Parameters.AddWithValue("$status", watchState.Status.ToString());
        command.Parameters.AddWithValue("$lastWatchedUtc", watchState.LastWatchedUtc?.UtcDateTime.ToString("O", CultureInfo.InvariantCulture) ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$lastSeasonNumber", watchState.LastSeasonNumber ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$lastEpisodeNumber", watchState.LastEpisodeNumber ?? (object)DBNull.Value);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task UpsertWatchlistEntryAsync(WatchlistEntry watchlistEntry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(watchlistEntry);
        await InitializeAsync(cancellationToken);

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO watchlist_entries (catalog_key, kind, added_utc, priority, notes)
            VALUES ($catalogKey, $kind, $addedUtc, $priority, $notes)
            ON CONFLICT(catalog_key) DO UPDATE SET
                added_utc = excluded.added_utc,
                priority = excluded.priority,
                notes = excluded.notes;
            """;
        command.Parameters.AddWithValue("$catalogKey", CatalogTitleKey.Create(watchlistEntry.Kind, watchlistEntry.Identifiers));
        command.Parameters.AddWithValue("$kind", watchlistEntry.Kind.ToString());
        command.Parameters.AddWithValue("$addedUtc", watchlistEntry.AddedUtc.UtcDateTime.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$priority", watchlistEntry.Priority);
        command.Parameters.AddWithValue("$notes", (object)watchlistEntry.Notes ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task DeleteWatchlistEntryAsync(
        TitleIdentifiers identifiers,
        TitleKind kind,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(identifiers);
        await InitializeAsync(cancellationToken);

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM watchlist_entries WHERE catalog_key = $catalogKey;";
        command.Parameters.AddWithValue("$catalogKey", CatalogTitleKey.Create(kind, identifiers));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task UpsertProviderAvailabilityAsync(
        TitleIdentifiers identifiers,
        TitleKind kind,
        IReadOnlyList<ProviderAvailability> providerAvailabilities,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(identifiers);
        ArgumentNullException.ThrowIfNull(providerAvailabilities);
        await InitializeAsync(cancellationToken);

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        var catalogKey = CatalogTitleKey.Create(kind, identifiers);

        var deleteCommand = connection.CreateCommand();
        deleteCommand.Transaction = transaction;
        deleteCommand.CommandText = "DELETE FROM provider_availability WHERE catalog_key = $catalogKey;";
        deleteCommand.Parameters.AddWithValue("$catalogKey", catalogKey);
        await deleteCommand.ExecuteNonQueryAsync(cancellationToken);

        foreach (var providerAvailability in providerAvailabilities)
        {
            var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText =
                """
                INSERT INTO provider_availability (
                    catalog_key, kind, region_code, provider_id, provider_name, logo_path, access_model, deep_link)
                VALUES (
                    $catalogKey, $kind, $regionCode, $providerId, $providerName, $logoPath, $accessModel, $deepLink);
                """;
            command.Parameters.AddWithValue("$catalogKey", catalogKey);
            command.Parameters.AddWithValue("$kind", kind.ToString());
            command.Parameters.AddWithValue("$regionCode", providerAvailability.RegionCode);
            command.Parameters.AddWithValue("$providerId", providerAvailability.Provider.ProviderId);
            command.Parameters.AddWithValue("$providerName", providerAvailability.Provider.Name);
            command.Parameters.AddWithValue("$logoPath", (object)providerAvailability.Provider.LogoPath ?? DBNull.Value);
            command.Parameters.AddWithValue("$accessModel", providerAvailability.AccessModel);
            command.Parameters.AddWithValue("$deepLink", (object)providerAvailability.DeepLink ?? DBNull.Value);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<LibraryItemSnapshot>> SearchLibraryAsync(
        string query,
        TitleKind kind,
        int maxResults,
        string directorFilter = null,
        CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        var normalizedQuery = query?.Trim();
        var normalizedDirectorFilter = directorFilter?.Trim();
        command.CommandText =
            """
            SELECT
                t.catalog_key,
                t.kind,
                t.tmdb_id,
                t.imdb_id,
                t.name,
                t.original_name,
                t.overview,
                t.release_date,
                t.poster_path,
                t.backdrop_path,
                t.genres,
                t.directors,
                t.original_language,
                t.public_rating,
                t.age_rating,
                t.runtime_minutes,
                t.season_count,
                t.episode_count,
                r.score_out_of_ten,
                r.notes AS rating_notes,
                r.updated_utc,
                ws.status,
                ws.last_watched_utc,
                ws.last_season_number,
                ws.last_episode_number,
                wl.added_utc,
                wl.priority,
                wl.notes AS watchlist_notes
            FROM titles t
            LEFT JOIN user_ratings r ON r.catalog_key = t.catalog_key
            LEFT JOIN watch_states ws ON ws.catalog_key = t.catalog_key
            LEFT JOIN watchlist_entries wl ON wl.catalog_key = t.catalog_key
            WHERE t.kind = $kind
              AND ($directorFilter = '' OR COALESCE(t.directors, '') LIKE $directorPattern)
              AND ($query = '' OR t.name LIKE $pattern OR COALESCE(t.original_name, '') LIKE $pattern OR COALESCE(t.directors, '') LIKE $pattern)
            ORDER BY
                CASE
                    WHEN $query = '' THEN 0
                    WHEN LOWER(t.name) = LOWER($query) THEN 0
                    WHEN LOWER(COALESCE(t.original_name, '')) = LOWER($query) THEN 1
                    WHEN LOWER(t.name) LIKE LOWER($prefixPattern) THEN 2
                    WHEN LOWER(COALESCE(t.original_name, '')) LIKE LOWER($prefixPattern) THEN 3
                    WHEN LOWER(t.name) LIKE LOWER($pattern) THEN 4
                    WHEN LOWER(COALESCE(t.original_name, '')) LIKE LOWER($pattern) THEN 5
                    WHEN LOWER(COALESCE(t.directors, '')) LIKE LOWER($pattern) THEN 6
                    ELSE 7
                END ASC,
                CASE WHEN $query = '' THEN t.updated_utc END DESC,
                CASE WHEN $query <> '' THEN t.name END ASC
            LIMIT $maxResults;
            """;
        command.Parameters.AddWithValue("$kind", kind.ToString());
        command.Parameters.AddWithValue("$directorFilter", normalizedDirectorFilter ?? string.Empty);
        command.Parameters.AddWithValue("$directorPattern", $"%{normalizedDirectorFilter}%");
        command.Parameters.AddWithValue("$query", normalizedQuery ?? string.Empty);
        command.Parameters.AddWithValue("$pattern", $"%{normalizedQuery}%");
        command.Parameters.AddWithValue("$prefixPattern", $"{normalizedQuery}%");
        command.Parameters.AddWithValue("$maxResults", maxResults);

        var results = new List<LibraryItemSnapshot>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            results.Add(await ReadSnapshotAsync(connection, reader, cancellationToken));

        return results;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<string, LibraryItemSnapshot>> GetByCatalogKeysAsync(
        IReadOnlyList<string> catalogKeys,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(catalogKeys);
        if (catalogKeys.Count == 0)
            return new Dictionary<string, LibraryItemSnapshot>(StringComparer.OrdinalIgnoreCase);

        await InitializeAsync(cancellationToken);

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        var parameterNames = new List<string>(catalogKeys.Count);
        for (var i = 0; i < catalogKeys.Count; i++)
        {
            var parameterName = $"$catalogKey{i}";
            parameterNames.Add(parameterName);
            command.Parameters.AddWithValue(parameterName, catalogKeys[i]);
        }

        command.CommandText =
            $"""
            SELECT
                t.catalog_key,
                t.kind,
                t.tmdb_id,
                t.imdb_id,
                t.name,
                t.original_name,
                t.overview,
                t.release_date,
                t.poster_path,
                t.backdrop_path,
                t.genres,
                t.directors,
                t.original_language,
                t.public_rating,
                t.age_rating,
                t.runtime_minutes,
                t.season_count,
                t.episode_count,
                r.score_out_of_ten,
                r.notes AS rating_notes,
                r.updated_utc,
                ws.status,
                ws.last_watched_utc,
                ws.last_season_number,
                ws.last_episode_number,
                wl.added_utc,
                wl.priority,
                wl.notes AS watchlist_notes
            FROM titles t
            LEFT JOIN user_ratings r ON r.catalog_key = t.catalog_key
            LEFT JOIN watch_states ws ON ws.catalog_key = t.catalog_key
            LEFT JOIN watchlist_entries wl ON wl.catalog_key = t.catalog_key
            WHERE t.catalog_key IN ({string.Join(", ", parameterNames)});
            """;

        var results = new Dictionary<string, LibraryItemSnapshot>(StringComparer.OrdinalIgnoreCase);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var snapshot = await ReadSnapshotAsync(connection, reader, cancellationToken);
            results[CatalogTitleKey.Create(snapshot.Title.Kind, snapshot.Title.Identifiers)] = snapshot;
        }

        return results;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<LibraryItemSnapshot>> GetWatchedAsync(
        string query,
        TitleKind kind,
        int maxResults,
        CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        var normalizedQuery = query?.Trim();
        command.CommandText =
            """
            SELECT
                t.catalog_key,
                t.kind,
                t.tmdb_id,
                t.imdb_id,
                t.name,
                t.original_name,
                t.overview,
                t.release_date,
                t.poster_path,
                t.backdrop_path,
                t.genres,
                t.directors,
                t.original_language,
                t.public_rating,
                t.age_rating,
                t.runtime_minutes,
                t.season_count,
                t.episode_count,
                r.score_out_of_ten,
                r.notes AS rating_notes,
                r.updated_utc,
                ws.status,
                ws.last_watched_utc,
                ws.last_season_number,
                ws.last_episode_number,
                wl.added_utc,
                wl.priority,
                wl.notes AS watchlist_notes
            FROM titles t
            LEFT JOIN user_ratings r ON r.catalog_key = t.catalog_key
            INNER JOIN watch_states ws ON ws.catalog_key = t.catalog_key
            LEFT JOIN watchlist_entries wl ON wl.catalog_key = t.catalog_key
            WHERE t.kind = $kind
              AND ws.status = $watchedStatus
              AND ($query = '' OR t.name LIKE $pattern OR COALESCE(t.original_name, '') LIKE $pattern OR COALESCE(t.directors, '') LIKE $pattern)
            ORDER BY
                CASE
                    WHEN $query = '' THEN 0
                    WHEN LOWER(t.name) = LOWER($query) THEN 0
                    WHEN LOWER(COALESCE(t.original_name, '')) = LOWER($query) THEN 1
                    WHEN LOWER(t.name) LIKE LOWER($prefixPattern) THEN 2
                    WHEN LOWER(COALESCE(t.original_name, '')) LIKE LOWER($prefixPattern) THEN 3
                    WHEN LOWER(t.name) LIKE LOWER($pattern) THEN 4
                    WHEN LOWER(COALESCE(t.original_name, '')) LIKE LOWER($pattern) THEN 5
                    WHEN LOWER(COALESCE(t.directors, '')) LIKE LOWER($pattern) THEN 6
                    ELSE 7
                END ASC,
                COALESCE(r.score_out_of_ten, -1) DESC,
                COALESCE(ws.last_watched_utc, r.updated_utc, t.updated_utc) DESC,
                t.name ASC
            LIMIT $maxResults;
            """;
        command.Parameters.AddWithValue("$kind", kind.ToString());
        command.Parameters.AddWithValue("$watchedStatus", WatchStatus.Watched.ToString());
        command.Parameters.AddWithValue("$query", normalizedQuery ?? string.Empty);
        command.Parameters.AddWithValue("$pattern", $"%{normalizedQuery}%");
        command.Parameters.AddWithValue("$prefixPattern", $"{normalizedQuery}%");
        command.Parameters.AddWithValue("$maxResults", maxResults);

        var results = new List<LibraryItemSnapshot>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            results.Add(await ReadSnapshotAsync(connection, reader, cancellationToken));

        return results;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<LibraryItemSnapshot>> GetWatchlistAsync(
        string query,
        TitleKind kind,
        int maxResults,
        CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        var normalizedQuery = query?.Trim();
        command.CommandText =
            """
            SELECT
                t.catalog_key,
                t.kind,
                t.tmdb_id,
                t.imdb_id,
                t.name,
                t.original_name,
                t.overview,
                t.release_date,
                t.poster_path,
                t.backdrop_path,
                t.genres,
                t.directors,
                t.original_language,
                t.public_rating,
                t.age_rating,
                t.runtime_minutes,
                t.season_count,
                t.episode_count,
                r.score_out_of_ten,
                r.notes AS rating_notes,
                r.updated_utc,
                ws.status,
                ws.last_watched_utc,
                ws.last_season_number,
                ws.last_episode_number,
                wl.added_utc,
                wl.priority,
                wl.notes AS watchlist_notes
            FROM titles t
            LEFT JOIN user_ratings r ON r.catalog_key = t.catalog_key
            LEFT JOIN watch_states ws ON ws.catalog_key = t.catalog_key
            INNER JOIN watchlist_entries wl ON wl.catalog_key = t.catalog_key
            WHERE t.kind = $kind
              AND ($query = '' OR t.name LIKE $pattern OR COALESCE(t.original_name, '') LIKE $pattern OR COALESCE(t.directors, '') LIKE $pattern)
            ORDER BY
                CASE
                    WHEN $query = '' THEN 0
                    WHEN LOWER(t.name) = LOWER($query) THEN 0
                    WHEN LOWER(COALESCE(t.original_name, '')) = LOWER($query) THEN 1
                    WHEN LOWER(t.name) LIKE LOWER($prefixPattern) THEN 2
                    WHEN LOWER(COALESCE(t.original_name, '')) LIKE LOWER($prefixPattern) THEN 3
                    WHEN LOWER(t.name) LIKE LOWER($pattern) THEN 4
                    WHEN LOWER(COALESCE(t.original_name, '')) LIKE LOWER($pattern) THEN 5
                    WHEN LOWER(COALESCE(t.directors, '')) LIKE LOWER($pattern) THEN 6
                    ELSE 7
                END ASC,
                COALESCE(wl.priority, 0) DESC,
                wl.added_utc DESC,
                t.name ASC
            LIMIT $maxResults;
            """;
        command.Parameters.AddWithValue("$kind", kind.ToString());
        command.Parameters.AddWithValue("$query", normalizedQuery ?? string.Empty);
        command.Parameters.AddWithValue("$pattern", $"%{normalizedQuery}%");
        command.Parameters.AddWithValue("$prefixPattern", $"{normalizedQuery}%");
        command.Parameters.AddWithValue("$maxResults", maxResults);

        var results = new List<LibraryItemSnapshot>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            results.Add(await ReadSnapshotAsync(connection, reader, cancellationToken));

        return results;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<RatedTitleInsight>> GetRatedTitleInsightsAsync(
        TitleKind kind,
        CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT
                t.catalog_key,
                t.name,
                t.release_date,
                t.genres,
                t.directors,
                r.score_out_of_ten
            FROM titles t
            INNER JOIN user_ratings r ON r.catalog_key = t.catalog_key
            WHERE t.kind = $kind
            ORDER BY COALESCE(r.updated_utc, t.updated_utc) DESC, t.name ASC;
            """;
        command.Parameters.AddWithValue("$kind", kind.ToString());

        var results = new List<RatedTitleInsight>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            int? releaseYear =
                reader.IsDBNull(reader.GetOrdinal("release_date"))
                    ? null
                    : DateOnly.Parse(reader.GetString(reader.GetOrdinal("release_date")), CultureInfo.InvariantCulture).Year;
            var genres = reader.IsDBNull(reader.GetOrdinal("genres"))
                ? Array.Empty<string>()
                : reader.GetString(reader.GetOrdinal("genres")).Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var directors = reader.IsDBNull(reader.GetOrdinal("directors"))
                ? Array.Empty<string>()
                : reader.GetString(reader.GetOrdinal("directors")).Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            results.Add(new RatedTitleInsight(
                reader.GetString(reader.GetOrdinal("catalog_key")),
                reader.GetString(reader.GetOrdinal("name")),
                reader.GetInt32(reader.GetOrdinal("score_out_of_ten")),
                releaseYear,
                genres,
                directors));
        }

        return results;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<LibraryItemSnapshot>> GetTitlesMissingMetadataAsync(
        TitleKind kind,
        int maxResults,
        CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        var today = DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        command.CommandText =
            """
            SELECT
                t.catalog_key,
                t.kind,
                t.tmdb_id,
                t.imdb_id,
                t.name,
                t.original_name,
                t.overview,
                t.release_date,
                t.poster_path,
                t.backdrop_path,
                t.genres,
                t.directors,
                t.original_language,
                t.public_rating,
                t.age_rating,
                t.runtime_minutes,
                t.season_count,
                t.episode_count,
                r.score_out_of_ten,
                r.notes AS rating_notes,
                r.updated_utc,
                ws.status,
                ws.last_watched_utc,
                ws.last_season_number,
                ws.last_episode_number,
                wl.added_utc,
                wl.priority,
                wl.notes AS watchlist_notes
            FROM titles t
            LEFT JOIN user_ratings r ON r.catalog_key = t.catalog_key
            LEFT JOIN watch_states ws ON ws.catalog_key = t.catalog_key
            LEFT JOIN watchlist_entries wl ON wl.catalog_key = t.catalog_key
            WHERE t.kind = $kind
              AND (
                    COALESCE(t.poster_path, '') = ''
                    OR (t.kind = 'Movie' AND COALESCE(t.age_rating, '') = '' AND (COALESCE(t.release_date, '') = '' OR t.release_date <= $today))
                    OR (t.kind = 'Movie' AND COALESCE(t.directors, '') = '')
                  )
            ORDER BY t.updated_utc ASC, t.name ASC
            LIMIT $maxResults;
            """;
        command.Parameters.AddWithValue("$kind", kind.ToString());
        command.Parameters.AddWithValue("$today", today);
        command.Parameters.AddWithValue("$maxResults", maxResults);

        var results = new List<LibraryItemSnapshot>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            results.Add(await ReadSnapshotAsync(connection, reader, cancellationToken));

        return results;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<LibraryItemSnapshot>> GetRatedTitlesMissingMetadataAsync(
        TitleKind kind,
        int maxResults,
        CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        var today = DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        command.CommandText =
            """
            SELECT
                t.catalog_key,
                t.kind,
                t.tmdb_id,
                t.imdb_id,
                t.name,
                t.original_name,
                t.overview,
                t.release_date,
                t.poster_path,
                t.backdrop_path,
                t.genres,
                t.directors,
                t.original_language,
                t.public_rating,
                t.age_rating,
                t.runtime_minutes,
                t.season_count,
                t.episode_count,
                r.score_out_of_ten,
                r.notes AS rating_notes,
                r.updated_utc,
                ws.status,
                ws.last_watched_utc,
                ws.last_season_number,
                ws.last_episode_number,
                wl.added_utc,
                wl.priority,
                wl.notes AS watchlist_notes
            FROM titles t
            INNER JOIN user_ratings r ON r.catalog_key = t.catalog_key
            LEFT JOIN watch_states ws ON ws.catalog_key = t.catalog_key
            LEFT JOIN watchlist_entries wl ON wl.catalog_key = t.catalog_key
            WHERE t.kind = $kind
              AND (
                    COALESCE(t.poster_path, '') = ''
                    OR (t.kind = 'Movie' AND COALESCE(t.age_rating, '') = '' AND (COALESCE(t.release_date, '') = '' OR t.release_date <= $today))
                    OR (t.kind = 'Movie' AND COALESCE(t.directors, '') = '')
                  )
            ORDER BY t.updated_utc ASC, t.name ASC
            LIMIT $maxResults;
            """;
        command.Parameters.AddWithValue("$kind", kind.ToString());
        command.Parameters.AddWithValue("$today", today);
        command.Parameters.AddWithValue("$maxResults", maxResults);

        var results = new List<LibraryItemSnapshot>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            results.Add(await ReadSnapshotAsync(connection, reader, cancellationToken));

        return results;
    }

    /// <inheritdoc />
    public Task ResetAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        Logger.Instance.Warn($"Resetting MovieG33k SQLite database at '{m_databaseFile.FullName}'.");

        SqliteConnection.ClearAllPools();
        DeleteIfPresent(m_databaseFile);
        DeleteIfPresent(new FileInfo($"{m_databaseFile.FullName}-wal"));
        DeleteIfPresent(new FileInfo($"{m_databaseFile.FullName}-shm"));
        DeleteIfPresent(new FileInfo($"{m_databaseFile.FullName}-journal"));

        m_isInitialized = false;
        return Task.CompletedTask;
    }

    private SqliteConnection CreateConnection() => new(m_connectionString);

    private static void DeleteIfPresent(FileInfo file)
    {
        if (file.Exists)
            file.Delete();
    }

    private static CatalogTitle MergeTitle(CatalogTitle existingTitle, CatalogTitle incomingTitle)
    {
        ArgumentNullException.ThrowIfNull(existingTitle);
        ArgumentNullException.ThrowIfNull(incomingTitle);

        var mergedIdentifiers = new TitleIdentifiers(
            incomingTitle.Identifiers.TmdbId ?? existingTitle.Identifiers.TmdbId,
            PreferText(incomingTitle.Identifiers.ImdbId, existingTitle.Identifiers.ImdbId));
        var mergedName = PreferText(incomingTitle.Name, existingTitle.Name);
        var mergedOriginalName = PreferText(incomingTitle.OriginalName, existingTitle.OriginalName);
        var mergedOverview = PreferText(incomingTitle.Overview, existingTitle.Overview);
        var mergedPosterPath = PreferText(incomingTitle.PosterPath, existingTitle.PosterPath);
        var mergedBackdropPath = PreferText(incomingTitle.BackdropPath, existingTitle.BackdropPath);
        var mergedGenres = PreferList(incomingTitle.Genres, existingTitle.Genres);
        var mergedLanguage = PreferText(incomingTitle.OriginalLanguage, existingTitle.OriginalLanguage);
        var mergedAgeRating = PreferAgeRating(incomingTitle.AgeRating, existingTitle.AgeRating);
        var mergedDirectors = PreferList(incomingTitle.Directors, existingTitle.Directors);

        return (existingTitle, incomingTitle) switch
        {
            (MovieEntry existingMovie, MovieEntry incomingMovie) => incomingMovie with
            {
                Identifiers = mergedIdentifiers,
                Name = mergedName,
                OriginalName = mergedOriginalName,
                Overview = mergedOverview,
                ReleaseDate = incomingMovie.ReleaseDate ?? existingMovie.ReleaseDate,
                PosterPath = mergedPosterPath,
                BackdropPath = mergedBackdropPath,
                Genres = mergedGenres,
                OriginalLanguage = mergedLanguage,
                RuntimeMinutes = incomingMovie.RuntimeMinutes is > 0 ? incomingMovie.RuntimeMinutes : existingMovie.RuntimeMinutes,
                PublicRating = incomingMovie.PublicRating ?? existingMovie.PublicRating,
                AgeRating = mergedAgeRating,
                Directors = mergedDirectors
            },
            (TvShowEntry existingShow, TvShowEntry incomingShow) => incomingShow with
            {
                Identifiers = mergedIdentifiers,
                Name = mergedName,
                OriginalName = mergedOriginalName,
                Overview = mergedOverview,
                ReleaseDate = incomingShow.ReleaseDate ?? existingShow.ReleaseDate,
                PosterPath = mergedPosterPath,
                BackdropPath = mergedBackdropPath,
                Genres = mergedGenres,
                OriginalLanguage = mergedLanguage,
                SeasonCount = incomingShow.SeasonCount ?? existingShow.SeasonCount,
                EpisodeCount = incomingShow.EpisodeCount ?? existingShow.EpisodeCount,
                PublicRating = incomingShow.PublicRating ?? existingShow.PublicRating,
                AgeRating = mergedAgeRating,
                Directors = mergedDirectors
            },
            _ => incomingTitle
        };
    }

    private static string PreferText(string incoming, string existing) =>
        string.IsNullOrWhiteSpace(incoming)
            ? NormalizeText(existing)
            : NormalizeText(incoming);

    private static string PreferAgeRating(string incoming, string existing)
    {
        var normalizedIncoming = NormalizeText(incoming);
        var normalizedExisting = NormalizeText(existing);
        if (string.IsNullOrWhiteSpace(normalizedIncoming))
            return normalizedExisting;

        return string.Equals(normalizedIncoming, CatalogTitle.UnknownAgeRating, StringComparison.OrdinalIgnoreCase) &&
               !string.IsNullOrWhiteSpace(normalizedExisting) &&
               !string.Equals(normalizedExisting, CatalogTitle.UnknownAgeRating, StringComparison.OrdinalIgnoreCase)
            ? normalizedExisting
            : normalizedIncoming;
    }

    private static IReadOnlyList<string> PreferList(IReadOnlyList<string> incoming, IReadOnlyList<string> existing)
    {
        var normalizedIncoming = NormalizeList(incoming);
        return normalizedIncoming.Count > 0
            ? normalizedIncoming
            : NormalizeList(existing);
    }

    private static string NormalizeText(string value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static IReadOnlyList<string> NormalizeList(IReadOnlyList<string> values) =>
        values?
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray()
        ?? Array.Empty<string>();

    private static void AddTitleParameters(SqliteCommand command, CatalogTitle title)
    {
        command.Parameters.AddWithValue("$catalogKey", CatalogTitleKey.Create(title.Kind, title.Identifiers));
        command.Parameters.AddWithValue("$kind", title.Kind.ToString());
        command.Parameters.AddWithValue("$tmdbId", title.Identifiers.TmdbId ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$imdbId", (object)title.Identifiers.ImdbId ?? DBNull.Value);
        command.Parameters.AddWithValue("$name", title.Name);
        command.Parameters.AddWithValue("$originalName", (object)title.OriginalName ?? DBNull.Value);
        command.Parameters.AddWithValue("$overview", (object)title.Overview ?? DBNull.Value);
        command.Parameters.AddWithValue("$releaseDate", title.ReleaseDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$posterPath", (object)title.PosterPath ?? DBNull.Value);
        command.Parameters.AddWithValue("$backdropPath", (object)title.BackdropPath ?? DBNull.Value);
        command.Parameters.AddWithValue("$genres", title.Genres == null ? DBNull.Value : string.Join('|', title.Genres));
        command.Parameters.AddWithValue("$directors", title.Directors == null ? DBNull.Value : string.Join('|', title.Directors));
        command.Parameters.AddWithValue("$originalLanguage", (object)title.OriginalLanguage ?? DBNull.Value);
        command.Parameters.AddWithValue("$publicRating", title.PublicRating ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$ageRating", (object)title.AgeRating ?? DBNull.Value);
        command.Parameters.AddWithValue("$updatedUtc", DateTimeOffset.UtcNow.UtcDateTime.ToString("O", CultureInfo.InvariantCulture));

        if (title is MovieEntry movieEntry)
        {
            command.Parameters.AddWithValue("$runtimeMinutes", movieEntry.RuntimeMinutes ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("$seasonCount", DBNull.Value);
            command.Parameters.AddWithValue("$episodeCount", DBNull.Value);
        }
        else if (title is TvShowEntry tvShowEntry)
        {
            command.Parameters.AddWithValue("$runtimeMinutes", DBNull.Value);
            command.Parameters.AddWithValue("$seasonCount", tvShowEntry.SeasonCount ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("$episodeCount", tvShowEntry.EpisodeCount ?? (object)DBNull.Value);
        }
        else
        {
            command.Parameters.AddWithValue("$runtimeMinutes", DBNull.Value);
            command.Parameters.AddWithValue("$seasonCount", DBNull.Value);
            command.Parameters.AddWithValue("$episodeCount", DBNull.Value);
        }
    }

    private static async Task<LibraryItemSnapshot> ReadSnapshotAsync(
        SqliteConnection connection,
        SqliteDataReader reader,
        CancellationToken cancellationToken)
    {
        var kind = Enum.Parse<TitleKind>(reader.GetString(reader.GetOrdinal("kind")));
        var identifiers = new TitleIdentifiers(
            reader.IsDBNull(reader.GetOrdinal("tmdb_id")) ? null : reader.GetInt32(reader.GetOrdinal("tmdb_id")),
            reader.IsDBNull(reader.GetOrdinal("imdb_id")) ? null : reader.GetString(reader.GetOrdinal("imdb_id")));
        var genres = reader.IsDBNull(reader.GetOrdinal("genres"))
            ? Array.Empty<string>()
            : reader.GetString(reader.GetOrdinal("genres")).Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var directors = reader.IsDBNull(reader.GetOrdinal("directors"))
            ? Array.Empty<string>()
            : reader.GetString(reader.GetOrdinal("directors")).Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        DateOnly? releaseDate = reader.IsDBNull(reader.GetOrdinal("release_date"))
            ? null
            : DateOnly.Parse(reader.GetString(reader.GetOrdinal("release_date")), CultureInfo.InvariantCulture);

        CatalogTitle title =
            kind == TitleKind.Movie
                ? new MovieEntry(
                    identifiers,
                    reader.GetString(reader.GetOrdinal("name")),
                    reader.IsDBNull(reader.GetOrdinal("original_name")) ? null : reader.GetString(reader.GetOrdinal("original_name")),
                    reader.IsDBNull(reader.GetOrdinal("overview")) ? null : reader.GetString(reader.GetOrdinal("overview")),
                    releaseDate,
                    reader.IsDBNull(reader.GetOrdinal("poster_path")) ? null : reader.GetString(reader.GetOrdinal("poster_path")),
                    reader.IsDBNull(reader.GetOrdinal("backdrop_path")) ? null : reader.GetString(reader.GetOrdinal("backdrop_path")),
                    genres,
                    reader.IsDBNull(reader.GetOrdinal("original_language")) ? null : reader.GetString(reader.GetOrdinal("original_language")),
                    reader.IsDBNull(reader.GetOrdinal("runtime_minutes")) ? null : reader.GetInt32(reader.GetOrdinal("runtime_minutes")),
                    reader.IsDBNull(reader.GetOrdinal("public_rating")) ? null : reader.GetDecimal(reader.GetOrdinal("public_rating")),
                    reader.IsDBNull(reader.GetOrdinal("age_rating")) ? null : reader.GetString(reader.GetOrdinal("age_rating")),
                    directors)
                : new TvShowEntry(
                    identifiers,
                    reader.GetString(reader.GetOrdinal("name")),
                    reader.IsDBNull(reader.GetOrdinal("original_name")) ? null : reader.GetString(reader.GetOrdinal("original_name")),
                    reader.IsDBNull(reader.GetOrdinal("overview")) ? null : reader.GetString(reader.GetOrdinal("overview")),
                    releaseDate,
                    reader.IsDBNull(reader.GetOrdinal("poster_path")) ? null : reader.GetString(reader.GetOrdinal("poster_path")),
                    reader.IsDBNull(reader.GetOrdinal("backdrop_path")) ? null : reader.GetString(reader.GetOrdinal("backdrop_path")),
                    genres,
                    reader.IsDBNull(reader.GetOrdinal("original_language")) ? null : reader.GetString(reader.GetOrdinal("original_language")),
                    reader.IsDBNull(reader.GetOrdinal("season_count")) ? null : reader.GetInt32(reader.GetOrdinal("season_count")),
                    reader.IsDBNull(reader.GetOrdinal("episode_count")) ? null : reader.GetInt32(reader.GetOrdinal("episode_count")),
                    reader.IsDBNull(reader.GetOrdinal("public_rating")) ? null : reader.GetDecimal(reader.GetOrdinal("public_rating")),
                    reader.IsDBNull(reader.GetOrdinal("age_rating")) ? null : reader.GetString(reader.GetOrdinal("age_rating")),
                    directors);

        UserRating rating = null;
        if (!reader.IsDBNull(reader.GetOrdinal("score_out_of_ten")))
        {
            rating = new UserRating(
                identifiers,
                kind,
                reader.GetInt32(reader.GetOrdinal("score_out_of_ten")),
                DateTimeOffset.Parse(reader.GetString(reader.GetOrdinal("updated_utc")), CultureInfo.InvariantCulture),
                reader.IsDBNull(reader.GetOrdinal("rating_notes")) ? null : reader.GetString(reader.GetOrdinal("rating_notes")));
        }

        WatchState watchState = null;
        if (!reader.IsDBNull(reader.GetOrdinal("status")))
        {
            watchState = new WatchState(
                identifiers,
                kind,
                Enum.Parse<WatchStatus>(reader.GetString(reader.GetOrdinal("status"))),
                reader.IsDBNull(reader.GetOrdinal("last_watched_utc"))
                    ? null
                    : DateTimeOffset.Parse(reader.GetString(reader.GetOrdinal("last_watched_utc")), CultureInfo.InvariantCulture),
                reader.IsDBNull(reader.GetOrdinal("last_season_number")) ? null : reader.GetInt32(reader.GetOrdinal("last_season_number")),
                reader.IsDBNull(reader.GetOrdinal("last_episode_number")) ? null : reader.GetInt32(reader.GetOrdinal("last_episode_number")));
        }

        WatchlistEntry watchlistEntry = null;
        if (!reader.IsDBNull(reader.GetOrdinal("added_utc")))
        {
            watchlistEntry = new WatchlistEntry(
                identifiers,
                kind,
                DateTimeOffset.Parse(reader.GetString(reader.GetOrdinal("added_utc")), CultureInfo.InvariantCulture),
                reader.GetInt32(reader.GetOrdinal("priority")),
                reader.IsDBNull(reader.GetOrdinal("watchlist_notes")) ? null : reader.GetString(reader.GetOrdinal("watchlist_notes")));
        }

        return new LibraryItemSnapshot(title, rating, watchState, watchlistEntry, Array.Empty<ProviderAvailability>());
    }
}
