// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.
//
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued!
//
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.

using DTC.Core;

namespace MovieG33k.Tests;

public sealed class SizedCacheTests
{
    [Test]
    public void SetEvictsLeastRecentlyUsedEntriesWhenTheBudgetIsExceeded()
    {
        var cache = new SizedCache<string>(10);

        cache.Set("a", "alpha", 4);
        cache.Set("b", "bravo", 4);

        Assert.That(cache.TryGetValue("a", out var alpha), Is.True);
        Assert.That(alpha, Is.EqualTo("alpha"));

        var evicted = cache.Set("c", "charlie", 4);

        Assert.That(evicted, Has.Count.EqualTo(1));
        Assert.That(evicted[0].Key, Is.EqualTo("b"));
        Assert.That(cache.TryGetValue("a", out _), Is.True);
        Assert.That(cache.TryGetValue("b", out _), Is.False);
        Assert.That(cache.TryGetValue("c", out var charlie), Is.True);
        Assert.That(charlie, Is.EqualTo("charlie"));
        Assert.That(cache.UsedCost, Is.EqualTo(8));
    }

    [Test]
    public void SetReplacingAnExistingKeyUpdatesTheStoredValueAndCost()
    {
        var cache = new SizedCache<string>(10);

        cache.Set("a", "alpha", 4);
        cache.Set("a", "updated", 7);

        Assert.That(cache.Count, Is.EqualTo(1));
        Assert.That(cache.UsedCost, Is.EqualTo(7));
        Assert.That(cache.TryGetValue("a", out var value), Is.True);
        Assert.That(value, Is.EqualTo("updated"));
    }
}
