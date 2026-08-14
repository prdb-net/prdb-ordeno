using System.Buffers;
using System.Text;

namespace Prdb.Ordeno.Core.Library;

/// <summary>
/// The two lossy steps every name in the library goes through: taking out what a
/// filesystem will not carry, and making what is left fit.
/// </summary>
/// <remarks>
/// Both are measured in section 8 of
/// <see href="https://github.com/prdb-net/prdb-ordeno/blob/main/docs/jellyfin-layout.md">the layout document</see>
/// rather than assumed. Jellyfin itself needed none of this — it served every
/// character class thrown at it — so what is defended against here is the
/// storage, and in particular an SMB share that accepts the reserved characters
/// and does not store them as written.
/// </remarks>
public static class LibraryNames
{
    /// <summary>
    /// What a single path component may weigh. **Bytes, not characters**: the
    /// limit was 255 bytes on ext4 and on the SMB share alike, and 85 CJK
    /// characters fit where 86 did not.
    /// </summary>
    public const int ComponentBudgetBytes = 255;

    /// <summary>
    /// What a scene directory has to leave free for the longest name derived from
    /// it: <c> - [2160p]</c> plus the longest extension the scanner accepts —
    /// <c>.mpeg</c>, <c>.m2ts</c>, <c>.webm</c> and <c>.divx</c> are all five
    /// bytes.
    /// </summary>
    /// <remarks>
    /// A constant, and deliberately not the length of the extension actually
    /// being filed: the same scene arriving as <c>.mkv</c> and as <c>.mpeg</c>
    /// must produce the same directory name, or the second quality of a scene
    /// whose title had to be truncated would land in a directory of its own.
    /// </remarks>
    public const int DerivedNameBytes = 15;

    /// <summary>What a scene directory name may weigh, once that room is kept.</summary>
    public const int SceneDirectoryBudgetBytes = ComponentBudgetBytes - DerivedNameBytes;

    /// <summary>
    /// The Windows-reserved set. Escaped whatever the target filesystem tolerates:
    /// the tool cannot see which options its mount was made with, and a library
    /// filed with these in it reads differently from another client — or is
    /// rejected outright by the same share mounted without <c>mapposix</c>.
    /// </summary>
    private static readonly SearchValues<char> Reserved = SearchValues.Create("<>:\"/\\|?*");

    /// <summary>
    /// One piece of arbitrary text — a title, a site — as a path component may
    /// carry it. Reserved characters and control characters become spaces rather
    /// than vanishing, so that <c>A/B</c> stays two words, and runs of whitespace
    /// then collapse to one.
    /// </summary>
    /// <remarks>
    /// The result may be empty, when a title was nothing but reserved characters.
    /// Callers give it a name of their own rather than putting an empty component
    /// in a path.
    /// </remarks>
    public static string Sanitise(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var kept = new StringBuilder(text.Length);

        foreach (var character in text)
        {
            // Surrogates fall through untouched: half of an astral character is
            // neither reserved nor a control character, and the pair is put back
            // together by the time anything measures it.
            var replaced = Reserved.Contains(character) || char.IsControl(character)
                ? ' '
                : character;

            if (replaced == ' ' && (kept.Length == 0 || kept[^1] == ' '))
            {
                continue;
            }

            kept.Append(replaced);
        }

        // A leading dot would hide the directory from a Linux file manager, from
        // Jellyfin's scanner, and from this tool's own walk, which skips dotted
        // names. A trailing dot or space is what the SMB share stores and other
        // clients disagree about.
        return kept.ToString().Trim().Trim('.').Trim();
    }

    /// <summary>
    /// The same name, cut to fit <paramref name="budgetBytes"/> when encoded as
    /// UTF-8, and left in a state a filesystem will carry.
    /// </summary>
    /// <remarks>
    /// Cutting happens between runes, never inside one, so what is left is always
    /// valid UTF-8 — a component truncated mid-sequence is not merely ugly, it is
    /// a name some of these filesystems refuse. What the cut exposes is then
    /// trimmed: a trailing space or period is the SMB problem again, and a
    /// trailing hyphen is the separator of a segment that is no longer there.
    /// </remarks>
    public static string Fit(string name, int budgetBytes)
    {
        if (Encoding.UTF8.GetByteCount(name) <= budgetBytes)
        {
            return TrimTail(name);
        }

        var kept = new StringBuilder(name.Length);
        var used = 0;

        foreach (var rune in name.EnumerateRunes())
        {
            if (used + rune.Utf8SequenceLength > budgetBytes)
            {
                break;
            }

            used += rune.Utf8SequenceLength;
            kept.Append(rune.ToString());
        }

        return TrimTail(kept.ToString());
    }

    private static string TrimTail(string name) => name.TrimEnd(' ', '.', '-', '_');
}
