// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.
//
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued!
//
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.

namespace MovieG33k.ViewModels;

/// <summary>
/// Presentation-friendly bar row for the insights screen.
/// </summary>
public sealed record InsightsBarViewModel(
    string Label,
    string ValueText,
    string DetailText,
    double Percent)
{
    public bool HasDetailText => !string.IsNullOrWhiteSpace(DetailText);
}
