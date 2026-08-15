using Prdb.Ordeno.Core.Configuration;

using Xunit;

namespace Prdb.Ordeno.Core.Tests.Configuration;

/// <summary>
/// The read model decides two things the rest of the tool leans on: whether
/// onboarding may be finished, and what the screen says while it may not. Both
/// are pure functions of what has been answered, which is why they are tested
/// here rather than through HTTP.
/// </summary>
public sealed class OrdenoConfigurationTests
{
    private static readonly DirectoryInspection UsableTarget =
        DirectoryInspection.Fine("/library", DirectoryRole.Target);

    [Fact]
    public void A_fresh_installation_is_waiting_for_the_api_key()
    {
        var configuration = new OrdenoConfiguration(
            ApiKeySet: false,
            Sources: [],
            Target: null,
            Layout: null,
            OnboardingCompletedAt: null);

        Assert.False(configuration.ReadyToComplete);
        Assert.False(configuration.Complete);
        Assert.Contains("Nothing is scanned yet", configuration.WhatHappensNext, StringComparison.Ordinal);
        Assert.Contains("API key", configuration.WhatHappensNext, StringComparison.Ordinal);
    }

    [Fact]
    public void A_key_without_a_source_asks_for_a_download_directory()
    {
        var configuration = new OrdenoConfiguration(
            ApiKeySet: true,
            Sources: [],
            Target: null,
            Layout: null,
            OnboardingCompletedAt: null);

        Assert.False(configuration.ReadyToComplete);
        Assert.Contains("downloads arrive in", configuration.WhatHappensNext, StringComparison.Ordinal);
    }

    /// <summary>
    /// A directory that was fine when it was added and is not fine now is the
    /// case this exists for: nothing in the database changed, and the tool must
    /// still refuse to call itself configured.
    /// </summary>
    [Fact]
    public void A_source_that_has_become_unreadable_stops_the_path_and_says_which()
    {
        var configuration = Configured(new ConfiguredSource(
            1,
            new DirectoryInspection("/downloads", DirectoryRole.Source, DirectoryProblem.NotReadable),
            FileMovement.Unknown));

        Assert.False(configuration.ReadyToComplete);
        Assert.Contains("/downloads", configuration.WhatHappensNext, StringComparison.Ordinal);
        Assert.Contains("PUID", configuration.WhatHappensNext, StringComparison.Ordinal);
    }

    [Fact]
    public void Everything_answered_ends_with_what_the_tool_will_do()
    {
        var configuration = Configured(Source(FileMovement.Rename));

        Assert.True(configuration.ReadyToComplete);
        Assert.False(configuration.Complete);
        Assert.Contains("will watch", configuration.WhatHappensNext, StringComparison.Ordinal);
        Assert.Contains("/library", configuration.WhatHappensNext, StringComparison.Ordinal);
        Assert.Contains("Jellyfin", configuration.WhatHappensNext, StringComparison.Ordinal);
        Assert.Contains("instant", configuration.WhatHappensNext, StringComparison.Ordinal);
    }

    /// <summary>
    /// ADR 0002: the difference between a rename and a copy is the difference
    /// between a millisecond and an hour, and this is where the user is told
    /// while they can still do something about it.
    /// </summary>
    [Fact]
    public void A_source_on_another_filesystem_says_so_before_anything_is_filed()
    {
        var configuration = Configured(Source(FileMovement.CopyThenDelete));

        Assert.True(configuration.ReadyToComplete);
        Assert.Contains("copied", configuration.WhatHappensNext, StringComparison.Ordinal);
        Assert.Contains("different filesystems", configuration.WhatHappensNext, StringComparison.Ordinal);
    }

    /// <summary>
    /// A finished path says what is true of this version and nothing beyond it.
    /// The tool watches and identifies — both run on their own — and it files
    /// nothing, so it claims exactly the first two and disclaims the third.
    /// Someone told their downloads are being handled stops looking at them.
    /// </summary>
    /// <summary>
    /// A finished setup watches on its own and files nothing on its own —
    /// ADR 0022. The sentence has to carry both halves, because someone who
    /// reads only the first goes away expecting their downloads to be dealt
    /// with while they sleep.
    /// </summary>
    [Fact]
    public void A_finished_path_says_that_filing_waits_to_be_asked()
    {
        var configuration = Configured(Source(FileMovement.Rename)) with
        {
            OnboardingCompletedAt = DateTimeOffset.UnixEpoch,
        };

        Assert.True(configuration.Complete);
        Assert.Contains("is watching", configuration.WhatHappensNext, StringComparison.Ordinal);
        Assert.Contains(
            "Filing happens when you ask for it",
            configuration.WhatHappensNext,
            StringComparison.Ordinal);
        Assert.Contains(
            "will not move anything by itself",
            configuration.WhatHappensNext,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Jellyfin")]
    [InlineData("jellyfin")]
    public void A_known_layout_is_recognised_however_it_is_written(string name) =>
        Assert.Equal(LibraryLayout.Jellyfin, LibraryLayouts.Parse(name));

    [Theory]
    [InlineData("Plex")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("0")]
    public void An_unknown_layout_is_no_layout(string? name) => Assert.Null(LibraryLayouts.Parse(name));

    private static ConfiguredSource Source(FileMovement movement) =>
        new(1, DirectoryInspection.Fine("/downloads", DirectoryRole.Source), movement);

    private static OrdenoConfiguration Configured(ConfiguredSource source) => new(
        ApiKeySet: true,
        Sources: [source],
        Target: UsableTarget,
        Layout: LibraryLayout.Jellyfin,
        OnboardingCompletedAt: null);
}
