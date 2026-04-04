// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.
//
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued!
//
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.

using System.Reflection;
using DTC.Core.Extensions;
using MovieG33k.Settings;

namespace MovieG33k.Tests;

public class MovieG33kSettingsTests
{
    [Test]
    public void DefaultsTmdbCredentialsToEmptyString()
    {
        var settingsFile = Assembly.GetEntryAssembly().GetAppSettingsPath().GetFile("movieg33k-settings.json");
        var originalContent = settingsFile.Exists() ? settingsFile.ReadAllText() : null;

        try
        {
            settingsFile.TryDelete();

            using var settings = new MovieG33kSettings();

            Assert.That(settings.TmdbAccessToken, Is.EqualTo(string.Empty));
            Assert.That(settings.TmdbApiKey, Is.EqualTo(string.Empty));
        }
        finally
        {
            RestoreSettingsFile(settingsFile, originalContent);
        }
    }

    [Test]
    public void SavesAndRestoresTmdbCredentials()
    {
        var settingsFile = Assembly.GetEntryAssembly().GetAppSettingsPath().GetFile("movieg33k-settings.json");
        var originalContent = settingsFile.Exists() ? settingsFile.ReadAllText() : null;

        try
        {
            using (var settings = new MovieG33kSettings())
            {
                settings.TmdbAccessToken = "access-token";
                settings.TmdbApiKey = "test-key-123";
                settings.Save();
            }

            using var restoredSettings = new MovieG33kSettings();

            Assert.That(restoredSettings.TmdbAccessToken, Is.EqualTo("access-token"));
            Assert.That(restoredSettings.TmdbApiKey, Is.EqualTo("test-key-123"));
        }
        finally
        {
            RestoreSettingsFile(settingsFile, originalContent);
        }
    }

    private static void RestoreSettingsFile(FileInfo settingsFile, string originalContent)
    {
        if (originalContent == null)
            settingsFile.TryDelete();
        else
            settingsFile.WriteAllText(originalContent);
    }
}
