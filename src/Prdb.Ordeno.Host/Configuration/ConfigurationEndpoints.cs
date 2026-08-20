using Microsoft.AspNetCore.Http.HttpResults;

using Prdb.Ordeno.Core.Configuration;
using Prdb.Ordeno.Core.Library;
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
/// The optional media server connection (ADR 0018), as far as the browser is
/// told about it: where it points, and nothing else. The key is a credential and
/// is never sent back, so an address being here is what "a connection is
/// configured" means.
/// </summary>
public sealed record MediaServerState(string Url);

/// <summary>
/// Everything onboarding has collected so far. The API key is not part of it and
/// never will be: the tool stores it and tells the browser only that it has one
/// (ADR 0009).
/// </summary>
/// <param name="Artwork">
/// Whether filing downloads one image per scene (ADR 0027). Not something
/// onboarding collects — the tool runs without it — so it is here for the
/// settings screen and false on every installation that never asked for it.
/// </param>
/// <param name="Unattended">
/// Whether the tool files without being asked (ADR 0031), and
/// <paramref name="UnattendedIntervalMinutes"/> is how often. Under the same
/// rule as the image switch and for a larger reason: this is the one setting
/// that lets the tool move files with nobody in front of it.
/// </param>
/// <param name="RefreshesMetadata">
/// Whether the tool checks what it filed against what prdb says now without
/// being asked (ADR 0032), and <paramref name="RefreshIntervalHours"/> is how
/// often. Its own switch rather than part of the one above: that one moves files
/// somebody downloaded, this one rewrites files the tool wrote itself.
/// </param>
public sealed record ConfigurationState(
    bool ApiKeySet,
    IReadOnlyList<SourceState> Sources,
    DirectoryState? Target,
    string? Layout,
    IReadOnlyList<LayoutOption> AvailableLayouts,
    MediaServerState? MediaServer,
    bool Artwork,
    bool Unattended,
    int UnattendedIntervalMinutes,
    bool RefreshesMetadata,
    int RefreshIntervalHours,
    bool Complete,
    bool ReadyToComplete,
    string WhatHappensNext);

/// <summary>
/// A refused change: why, and the configuration as it still stands. Both,
/// because the screen has to show a message and stay true to what is stored,
/// and a second request to find out would be a second answer to disagree with.
/// </summary>
public sealed record ConfigurationProblem(string Message, ConfigurationState Configuration);

/// <summary>
/// What the connection test found, in words for the person who is standing in
/// front of it, and as a status the screen can pick a colour from.
/// </summary>
/// <param name="Status">
/// One of <c>Working</c>, <c>Unproven</c>, <c>Unmatched</c> or
/// <c>DatesDiscarded</c>. The two that never arrive here are <c>Refused</c> and
/// <c>Unreachable</c>: nothing is stored for those, so they come back as a
/// refusal instead.
/// </param>
/// <param name="Working">
/// Everything was proved rather than assumed — the key works, the dates will be
/// read, and the server holds something this tool filed.
/// </param>
public sealed record MediaServerCheckState(
    string Status,
    string Message,
    bool Working,
    ConfigurationState Configuration);

public sealed record SetApiKeyRequest(string ApiKey);

public sealed record AddSourceRequest(string Path);

public sealed record SetTargetRequest(string Path, string Layout);

/// <summary>
/// The artwork switch. A field rather than a bare <c>PUT</c> and <c>DELETE</c>
/// pair, because this is one setting with two values and not a thing that exists
/// or does not.
/// </summary>
public sealed record SetArtworkRequest(bool Enabled);

/// <summary>The unattended filing switch, in the same shape and for the same reason.</summary>
public sealed record SetUnattendedFilingRequest(bool Enabled);

/// <summary>The unattended metadata refresh switch — ADR 0032, same shape again.</summary>
public sealed record SetUnattendedRefreshRequest(bool Enabled);

/// <summary>
/// Both fields together, because neither is any use alone. Sending them empty is
/// not how a connection is removed — that is the <c>DELETE</c>, which says what
/// it means.
/// </summary>
public sealed record SetMediaServerRequest(string Url, string ApiKey);

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

        // ADR 0027's switch. It lives with the library settings because it is a
        // property of what filing writes, and it answers with the whole
        // configuration like every other change here.
        configuration.MapPut("/artwork", async Task<Results<Ok<ConfigurationState>, BadRequest<ConfigurationProblem>>> (
            SetArtworkRequest request,
            ConfigurationService service,
            CancellationToken cancellationToken) =>
            Answer(await service.SetArtworkAsync(request.Enabled, cancellationToken)));

        // ADR 0031's switch, next to ADR 0027's and for the same reason: it is a
        // property of what filing does, not something onboarding waits for an
        // answer to. Turning it on is the only setting in the tool that lets it
        // move a file with nobody in front of it.
        configuration.MapPut("/unattended-filing", async Task<Results<Ok<ConfigurationState>, BadRequest<ConfigurationProblem>>> (
            SetUnattendedFilingRequest request,
            ConfigurationService service,
            CancellationToken cancellationToken) =>
            Answer(await service.SetUnattendedFilingAsync(request.Enabled, cancellationToken)));

        // ADR 0032's switch, next to it. What it turns on rewrites metadata files
        // the tool wrote itself and writes images where there are none; it moves
        // nothing, which is why it is a smaller decision than the one above.
        configuration.MapPut("/unattended-refresh", async Task<Results<Ok<ConfigurationState>, BadRequest<ConfigurationProblem>>> (
            SetUnattendedRefreshRequest request,
            ConfigurationService service,
            CancellationToken cancellationToken) =>
            Answer(await service.SetUnattendedRefreshAsync(request.Enabled, cancellationToken)));

        // ADR 0018's two optional fields. Nothing here is on the filing path, and
        // a setup that never touches these endpoints is a finished setup.
        configuration.MapPut("/media-server", async Task<Results<Ok<MediaServerCheckState>, BadRequest<ConfigurationProblem>>> (
            SetMediaServerRequest request,
            ConfigurationService service,
            CancellationToken cancellationToken) =>
            Answer(await service.SetMediaServerAsync(request.Url, request.ApiKey, cancellationToken)));

        // Worth asking again later: a key can be revoked and a library can be
        // pointed elsewhere without anybody touching this tool.
        configuration.MapPost("/media-server/test", async Task<Results<Ok<MediaServerCheckState>, BadRequest<ConfigurationProblem>>> (
            ConfigurationService service,
            CancellationToken cancellationToken) =>
            Answer(await service.CheckMediaServerAsync(cancellationToken)));

        // Back to blank, which is what most installations run as.
        configuration.MapDelete("/media-server", async Task<Results<Ok<ConfigurationState>, BadRequest<ConfigurationProblem>>> (
            ConfigurationService service,
            CancellationToken cancellationToken) =>
            Answer(await service.ForgetMediaServerAsync(cancellationToken)));

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

    /// <summary>
    /// A stored connection answers with what the server said, and a refused one
    /// with why — including a server that answered "no", which is a change that
    /// was not made rather than a report about one that was.
    /// </summary>
    private static Results<Ok<MediaServerCheckState>, BadRequest<ConfigurationProblem>> Answer(
        MediaServerChange change)
    {
        var state = StateOf(change.Configuration);

        return change.Accepted
            ? TypedResults.Ok(new MediaServerCheckState(
                change.Check!.Status.ToString(),
                change.Check.Message,
                change.Check.Working,
                state))
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
        MediaServer: configuration.MediaServerUrl is { } url ? new MediaServerState(url) : null,
        Artwork: configuration.Artwork,
        Unattended: configuration.Unattended,
        UnattendedIntervalMinutes: (int)FilingSchedule.Interval.TotalMinutes,
        RefreshesMetadata: configuration.RefreshesMetadata,
        RefreshIntervalHours: (int)RefreshSchedule.Interval.TotalHours,
        Complete: configuration.Complete,
        ReadyToComplete: configuration.ReadyToComplete,
        WhatHappensNext: configuration.WhatHappensNext);
}
