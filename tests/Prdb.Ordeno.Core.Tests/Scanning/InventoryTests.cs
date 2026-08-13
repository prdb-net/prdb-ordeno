using Prdb.Ordeno.Core.Scanning;

using Xunit;

namespace Prdb.Ordeno.Core.Tests.Scanning;

/// <summary>
/// The sentence at the top of the screen. It is tested because it is the whole
/// answer for most visits — someone opens the tool to find out whether their
/// downloads are being dealt with, and this line is what they read.
/// </summary>
public sealed class InventoryTests
{
    [Fact]
    public void An_unfinished_setup_says_so_rather_than_reporting_an_empty_directory()
    {
        var inventory = new Inventory(OnboardingComplete: false, Sources: [], Files: []);

        Assert.Contains("Finish the setup", inventory.WhatItFound, StringComparison.Ordinal);
    }

    [Fact]
    public void The_counts_add_up_across_directories()
    {
        var inventory = Found(Source(1, ready: 40, settling: 2), Source(2, ready: 3, settling: 0));

        Assert.Equal(43, inventory.Ready);
        Assert.Equal(2, inventory.Settling);
        Assert.Equal(45, inventory.Total);
        Assert.Contains("45 videos", inventory.WhatItFound, StringComparison.Ordinal);
        Assert.Contains("43 finished downloading", inventory.WhatItFound, StringComparison.Ordinal);
    }

    /// <summary>
    /// A library-sized number is read, not counted. Thousands of files is where
    /// this tool earns its keep, and "4000 videos" is harder to take in than the
    /// same number with a separator in it.
    /// </summary>
    [Fact]
    public void A_large_number_is_grouped()
    {
        var inventory = Found(Source(1, ready: 4_218, settling: 0));

        Assert.Contains("4,218 videos", inventory.WhatItFound, StringComparison.Ordinal);
    }

    /// <summary>
    /// The claim the tool must never make by implication. Someone who reads a
    /// list of their downloads in a tool that promises to file them concludes it
    /// is filing them, and reports the absence months later as data loss.
    /// </summary>
    [Fact]
    public void Anything_found_is_reported_as_untouched()
    {
        var inventory = Found(Source(1, ready: 1, settling: 0));

        Assert.Contains("Nothing is identified or filed yet", inventory.WhatItFound, StringComparison.Ordinal);
    }

    [Fact]
    public void A_directory_that_cannot_be_read_is_not_reported_as_empty()
    {
        var inventory = new Inventory(
            OnboardingComplete: true,
            Sources: [new ScannedSource(1, "/downloads", Reachable: false, "The share is gone.", 0, 0)],
            Files: []);

        Assert.Contains("cannot be read", inventory.WhatItFound, StringComparison.Ordinal);
        Assert.DoesNotContain("No videos", inventory.WhatItFound, StringComparison.Ordinal);
    }

    /// <summary>
    /// One unreachable directory among several does not hide the others, but it
    /// does have to qualify the count — the number below it is not the whole
    /// picture, and saying so is the difference between a number and a wrong one.
    /// </summary>
    [Fact]
    public void One_unreachable_directory_qualifies_the_count()
    {
        var inventory = new Inventory(
            OnboardingComplete: true,
            Sources:
            [
                Source(1, ready: 5, settling: 0),
                new ScannedSource(2, "/other", Reachable: false, "The share is gone.", 0, 0),
            ],
            Files: []);

        Assert.Contains("5 videos", inventory.WhatItFound, StringComparison.Ordinal);
        Assert.Contains("1 download directory could not be read", inventory.WhatItFound, StringComparison.Ordinal);
    }

    [Fact]
    public void Nothing_found_is_not_a_problem_to_report()
    {
        var inventory = Found(Source(1, ready: 0, settling: 0));

        Assert.Contains("No videos", inventory.WhatItFound, StringComparison.Ordinal);
    }

    private static ScannedSource Source(int id, int ready, int settling) =>
        new(id, $"/downloads/{id}", Reachable: true, Problem: null, ready, settling);

    private static Inventory Found(params ScannedSource[] sources) =>
        new(OnboardingComplete: true, Sources: sources, Files: []);
}
