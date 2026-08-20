using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using Prdb.Ordeno.Core.Library;
using Prdb.Ordeno.Core.Review;
using Prdb.Ordeno.Infrastructure.Configuration;
using Prdb.Ordeno.Infrastructure.MediaServer;
using Prdb.Ordeno.Infrastructure.Review;

namespace Prdb.Ordeno.Infrastructure.Library;

public static class LibraryServiceCollectionExtensions
{
    /// <summary>
    /// How long one image may take. Long enough for a picture over a domestic
    /// connection, short enough that a CDN that has stopped answering is a
    /// sentence on a row rather than a filing run that never ends.
    /// </summary>
    private static readonly TimeSpan ArtworkTimeout = TimeSpan.FromMinutes(2);

    /// <summary>
    /// Filing: what would happen, what carries it out, and the two files that go
    /// in next to what moved.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The runner is a singleton because it is the gate — one run at a time, and
    /// a status the screen reads while it works. The service is scoped, like
    /// everything that holds a database context.
    /// </para>
    /// <para>
    /// Filing asks prdb what it is filing, so this slice needs
    /// <see cref="IVideoLookup"/> — the review queue's own registration, and
    /// deliberately not a second client. Both are the one transport that refuses
    /// a redirect off the prdb host while the key is on the request.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddOrdenoLibrary(this IServiceCollection services)
    {
        // The same connection every other caller of prdb sends through, and
        // registering it twice is registering it once — which is what makes this
        // slice usable without the review one.
        services.AddPrdbTransport();
        services.TryAddSingleton<IVideoLookup, PrdbVideoLookup>();

        services.TryAddSingleton<ISceneDirectories, SceneDirectories>();
        services.TryAddSingleton<IVideoQualities, VideoQualities>();
        services.TryAddSingleton<TargetPaths>();
        services.TryAddSingleton<FilingPlanner>();
        services.TryAddSingleton<LibraryMoves>();

        // One object under both names: the planner asks it what is at a path and
        // the run asks it to write, and a second instance would be a second
        // answer to the same question.
        services.TryAddSingleton<Sidecars>();
        services.TryAddSingleton<ISidecars>(provider => provider.GetRequiredService<Sidecars>());

        // The image, under the same arrangement — ADR 0027. Its connection is
        // its own: it goes to a CDN rather than to the API, carries no key, and
        // therefore follows redirects, which is exactly what the other two
        // transports refuse to do and for exactly the opposite reason. The
        // timeout is generous because this is a file on somebody's slow line and
        // nobody is waiting in front of a screen for it.
        services.AddHttpClient(SceneArtwork.HttpClientName, client => client.Timeout = ArtworkTimeout);

        services.TryAddSingleton<SceneArtwork>();
        services.TryAddSingleton<ISceneArtwork>(provider => provider.GetRequiredService<SceneArtwork>());

        // One gate over everything that rearranges the library, shared with the
        // way back — ADR 0029. Two of them would be no gate at all.
        services.TryAddSingleton<LibraryGate>();

        services.TryAddSingleton<FilingRunner>();
        services.TryAddScoped<FilingService>();

        // The refresh (ADR 0032), in the same two halves and behind the same
        // gate: one run at a time over one library, whether it is filing, undoing
        // or bringing metadata up to date.
        services.TryAddSingleton<RefreshRunner>();
        services.TryAddScoped<RefreshService>();

        // Filing writes the operation log as it goes (ADR 0028), so the writer
        // is part of this slice rather than something the host remembers to add.
        services.TryAddScoped<History.OperationLog>();

        // The optional half: a run that has finished tells the media server what
        // it wrote, if there is one to tell. Registered here because the runner
        // reaches for it, and doing nothing when nothing is configured is the
        // service's own answer rather than a wiring decision.
        services.AddOrdenoMediaServer();

        return services;
    }
}
