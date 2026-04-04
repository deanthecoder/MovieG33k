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

namespace MovieG33k.Core.Services;

/// <summary>
/// Describes a small disk-backed poster cache.
/// </summary>
public interface IPosterCache
{
    /// <summary>
    /// Returns a cached poster file, downloading and storing it if needed.
    /// </summary>
    Task<FileInfo> GetOrAddAsync(
        string cacheKey,
        Func<CancellationToken, Task<Stream>> loader,
        CancellationToken cancellationToken = default);
}
