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
using System.Reflection;
using DTC.Core.Extensions;

namespace MovieG33k.Data.Services;

/// <summary>
/// Resolves the default SQLite database location for MovieG33k.
/// </summary>
/// <remarks>
/// This keeps app-data path logic in one place so the repository can stay focused on storage concerns.
/// </remarks>
public sealed class MovieG33kDatabasePathResolver
{
    /// <summary>
    /// Returns the database file path used by the app by default.
    /// </summary>
    public FileInfo GetDefaultDatabaseFile() =>
        Assembly.GetEntryAssembly()
            .GetAppSettingsPath()
            .GetFile("movieg33k.db");
}
