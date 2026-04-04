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
using System.Reflection;
using DTC.Core;
using DTC.Core.Extensions;
using MovieG33k.Core.Services;

namespace MovieG33k.Data.Services;

/// <summary>
/// Disk-backed poster cache with a fixed size budget.
/// </summary>
public sealed class MoviePosterCache : IPosterCache
{
    private const long DefaultMaxCacheBytes = 50L * 1024 * 1024;

    private readonly DirectoryInfo m_cacheDirectory;
    private readonly SizedCache<FileInfo> m_cache;
    private readonly SemaphoreSlim m_gate = new(1, 1);
    private bool m_isInitialized;

    /// <summary>
    /// Creates a poster cache under the app data folder.
    /// </summary>
    public MoviePosterCache(long maxCacheBytes = DefaultMaxCacheBytes, DirectoryInfo cacheDirectory = null)
    {
        if (maxCacheBytes <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxCacheBytes), "The cache budget must be greater than zero.");

        m_cacheDirectory = cacheDirectory ?? CreateDefaultCacheDirectory();
        m_cacheDirectory.Create();
        m_cache = new SizedCache<FileInfo>(maxCacheBytes);
    }

    /// <inheritdoc />
    public async Task<FileInfo> GetOrAddAsync(
        string cacheKey,
        Func<CancellationToken, Task<Stream>> loader,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(cacheKey))
            throw new ArgumentException("A cache key is required.", nameof(cacheKey));

        if (loader == null)
            throw new ArgumentNullException(nameof(loader));

        await EnsureInitializedAsync(cancellationToken);

        var normalizedKey = NormalizeKey(cacheKey);
        if (TryGetCachedFile(normalizedKey, out var cachedFile))
        {
            Logger.Instance.Info($"Poster cache hit for '{cacheKey}'.");
            return cachedFile;
        }

        Logger.Instance.Info($"Poster cache miss for '{cacheKey}'.");

        var tempFile = m_cacheDirectory.GetFile($"{normalizedKey}.{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var sourceStream = await loader(cancellationToken))
            {
                if (sourceStream == null)
                    return null;

                await using var destinationStream = tempFile.Open(FileMode.Create, FileAccess.Write, FileShare.None);
                await sourceStream.CopyToAsync(destinationStream, cancellationToken);
            }

            var posterFile = m_cacheDirectory.GetFile($"{normalizedKey}.poster");
            await m_gate.WaitAsync(cancellationToken);
            try
            {
                if (posterFile.Exists())
                {
                    tempFile.TryDelete();
                    posterFile.LastWriteTimeUtc = DateTime.UtcNow;
                    m_cache.Set(normalizedKey, posterFile, posterFile.Length);
                    return posterFile;
                }

                File.Move(tempFile.FullName, posterFile.FullName, overwrite: true);
                posterFile.Refresh();
                posterFile.LastWriteTimeUtc = DateTime.UtcNow;

                var evictedItems = m_cache.Set(normalizedKey, posterFile, posterFile.Length);
                DeleteEvictedFiles(evictedItems);

                if (posterFile.Exists())
                {
                    Logger.Instance.Info($"Cached poster '{cacheKey}' ({posterFile.Length.ToString("N0", CultureInfo.InvariantCulture)} bytes).");
                    return posterFile;
                }

                return null;
            }
            finally
            {
                m_gate.Release();
            }
        }
        finally
        {
            tempFile.TryDelete();
        }
    }

    private async Task EnsureInitializedAsync(CancellationToken cancellationToken)
    {
        if (m_isInitialized)
            return;

        await m_gate.WaitAsync(cancellationToken);
        try
        {
            if (m_isInitialized)
                return;

            m_cacheDirectory.Create();
            foreach (var posterFile in m_cacheDirectory.GetFiles("*.poster").OrderBy(file => file.LastWriteTimeUtc))
            {
                var cacheKey = posterFile.LeafName();
                var evictedItems = m_cache.Set(cacheKey, posterFile, posterFile.Length);
                DeleteEvictedFiles(evictedItems);
            }

            m_isInitialized = true;
        }
        finally
        {
            m_gate.Release();
        }
    }

    private bool TryGetCachedFile(string cacheKey, out FileInfo cachedFile)
    {
        if (!m_cache.TryGetValue(cacheKey, out cachedFile))
            return false;

        if (!cachedFile.Exists())
        {
            m_cache.Remove(cacheKey);
            cachedFile = null;
            return false;
        }

        cachedFile.LastWriteTimeUtc = DateTime.UtcNow;
        return true;
    }

    private void DeleteEvictedFiles(IEnumerable<SizedCache<FileInfo>.CacheItem> evictedItems)
    {
        foreach (var evictedItem in evictedItems ?? Array.Empty<SizedCache<FileInfo>.CacheItem>())
        {
            if (evictedItem.Value == null)
                continue;

            if (evictedItem.Value.Exists())
            {
                Logger.Instance.Info($"Evicting cached poster '{evictedItem.Key}'.");
                evictedItem.Value.TryDelete();
            }
        }
    }

    private static string NormalizeKey(string cacheKey) =>
        cacheKey.Trim().Fnv1a64().ToString("X16", CultureInfo.InvariantCulture);

    private static DirectoryInfo CreateDefaultCacheDirectory() =>
        Assembly.GetEntryAssembly()
            .GetAppSettingsPath()
            .GetDir("cache")
            .GetDir("posters");
}
