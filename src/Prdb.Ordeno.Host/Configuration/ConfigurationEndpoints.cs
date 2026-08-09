using Microsoft.AspNetCore.Http.HttpResults;

using Prdb.Ordeno.Core.Configuration;
using Prdb.Ordeno.Infrastructure.Configuration;

namespace Prdb.Ordeno.Host.Configuration;

/// <summary>
/// One directory as the tool last found it.
/// </summary>
/// <param name="Problem">
/// What is wrong with it, in words for the user. <c>null</c> when there is
/// nothing wrong.
/// </param>
public sealed record DirectoryState(string Path, bool Usable, string? Problem);

/// <summary>
/// A watched directory, and what filing a video out of it will cost — see
/// ADR 0002. <paramref name="Movement"/> is the machine-readable answer,
/// <paramref name="MovementExplained"/> the one to put on the screen.
/// </summary>
public sealed record SourceState(
    int Id,
    string Path,
    bool Usable,
    string? Problem,
    string Movement,
    string MovementExplained);

public sealed record LayoutOption(string Name, string Description);

/// <summary>
/// Everything onboarding has collected so far. The API key is not part of it and
/// never will be: the tool stores it and tells the browser only that it has one
/// (ADR 0009).
/// </summary>
public sealed record ConfigurationState(
    bool ApiKeySet,
    IReadOnlyList<SourceState> Sources,
    DirectoryState? Target,
    string? Layout,
    IReadOnlyList<LayoutOption> AvailableLayouts,
    bool Complete,
    bool ReadyToComplete,
    string WhatHappensNext);

/// <summary>
/// A refused change: why, and the configuration as it still stands. Both,
/// because the screen has to show a message and stay true to what is stored,
/// and a second request to find out would be a second answer to disagree with.
/// </summary>
public sealed record ConfigurationProblem(string Message, ConfigurationState Configuration);

public sealed record SetApiKeyRequest(string ApiKey);

public sealed record AddSourceRequest(string Path);

public sealed record SetTargetRequest(string Path, string Layout);

internal static class ConfigurationEndpoints
{
    /// <summary>
    /// Nothing here says <c>AllowAnonymous</c>. Onboarding happens after the
    /// password has been set — the first visitor sets it and is signed in by
    /// doing so (ADR 0010) — so a stranger reaching these would be a stranger
    /// pointing this tool at directories.
    /// </summary>
    public static IEndpointRouteBuilder MapConfiguration(this IEndpointRouteBuilder endpoints)
    {
        var configuration = endpoints.MapGroup("/api/configuration").WithTags("Configuration");

        configuration.MapGet("/", async Task<Ok<ConfigurationState>> (
            ConfigurationService service,
            CancellationToken cancellationToken) =>
            TypedResults.Ok(StateOf(await service.ReadAsync(cancellationToken))));

        configuration.MapPut("/api-key", async Task<Results<Ok<ConfigurationState>, BadRequest<ConfigurationProblem>>> (
            SetApiKeyRequest request,
            ConfigurationService service,
            CancellationToken cancellationToken) =>
            Answer(await service.SetApiKeyAsync(request.ApiKey, cancellationToken)));

        configuration.MapPost("/sources", async Task<Results<Ok<ConfigurationState>, BadRequest<ConfigurationProblem>>> (
            AddSourceRequest request,
            ConfigurationService service,
            CancellationToken cancellationToken) =>
            Answer(await service.AddSourceAsync(request.Path, cancellationToken)));

        // Idempotent on purpose: a directory that is already gone leaves the
        // caller with the answer it asked for, which is a configuration without it.
        configuration.MapDelete("/sources/{id:int}", async Task<Results<Ok<ConfigurationState>, BadRequest<ConfigurationProblem>>> (
            int id,
            ConfigurationService service,
            CancellationToken cancellationToken) =>
            Answer(await service.RemoveSourceAsync(id, cancellationToken)));

        configuration.MapPut("/target", async Task<Results<Ok<ConfigurationState>, BadRequest<ConfigurationProblem>>> (
            SetTargetRequest request,
            ConfigurationService service,
            CancellationToken cancellationToken) =>
            Answer(await service.SetTargetAsync(request.Path, request.Layout, cancellationToken)));

        // The end of the guided path. It answers 400 while anything is missing,
        // carrying the sentence that says what — the same sentence the screen
        // was already showing.
        configuration.MapPost("/completion", async Task<Results<Ok<ConfigurationState>, BadRequest<ConfigurationProblem>>> (
            ConfigurationService service,
            CancellationToken cancellationToken) =>
            Answer(await service.CompleteOnboardingAsync(cancellationToken)));

        return endpoints;
    }

    private static Results<Ok<ConfigurationState>, BadRequest<ConfigurationProblem>> Answer(ConfigurationChange change)
    {
        var state = StateOf(change.Configuration);

        return change.Accepted
            ? TypedResults.Ok(state)
            : TypedResults.BadRequest(new ConfigurationProblem(change.Message!, state));
    }

    private static ConfigurationState StateOf(OrdenoConfiguration configuration) => new(
        ApiKeySet: configuration.ApiKeySet,
        Sources:
        [
            .. configuration.Sources.Select(source => new SourceState(
                source.Id,
                source.Inspection.Path,
                source.Inspection.Usable,
                source.Inspection.Message,
                source.Movement.ToString(),
                FileMovements.Describe(source.Movement))),
        ],
        Target: configuration.Target is { } target
            ? new DirectoryState(target.Path, target.Usable, target.Message)
            : null,
        Layout: configuration.Layout is { } layout ? LibraryLayouts.NameOf(layout) : null,
        AvailableLayouts:
        [
            .. LibraryLayouts.All.Select(choice => new LayoutOption(choice.Name, choice.Description)),
        ],
        Complete: configuration.Complete,
        ReadyToComplete: configuration.ReadyToComplete,
        WhatHappensNext: configuration.WhatHappensNext);
}
