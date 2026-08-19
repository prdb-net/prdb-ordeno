namespace Prdb.Ordeno.Infrastructure.Library;

/// <summary>
/// One thing at a time may rearrange the library.
/// </summary>
/// <remarks>
/// <para>
/// Filing held this itself until there was a second thing that moves files. Now
/// the way back (ADR 0029) takes the same gate rather than one of its own: two
/// runs at once would have two copies of a plan moving the same file, and a
/// preview worked out while an undo is under way describes a library that is
/// being rearranged underneath it.
/// </para>
/// <para>
/// A singleton, and deliberately not a lock held across awaits: whoever gets in
/// starts a background run and leaves the gate closed until that run finishes,
/// which is what makes "something is already under way" an answer a request can
/// be given immediately.
/// </para>
/// </remarks>
public sealed class LibraryGate
{
    private readonly SemaphoreSlim gate = new(1, 1);

    /// <summary><c>false</c> when something is already under way.</summary>
    public bool TryEnter() => gate.Wait(0, CancellationToken.None);

    public void Leave() => gate.Release();
}
