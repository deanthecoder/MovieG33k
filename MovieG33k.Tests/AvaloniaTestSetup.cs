// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.
//
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued!
//
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.

using Avalonia;
using Avalonia.Headless;

[assembly: AvaloniaTestApplication(typeof(MovieG33k.Tests.AvaloniaTestSetup))]

namespace MovieG33k.Tests;

public static class AvaloniaTestSetup
{
    public static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder.Configure<Views.App>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions
            {
                UseHeadlessDrawing = false
            });
    }
}
