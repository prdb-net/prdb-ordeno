using System.Globalization;
using System.Text;
using System.Xml;

namespace Prdb.Ordeno.Core.Library;

/// <summary>
/// The sidecar Jellyfin reads, as a document: <c>movie.nfo</c>, root element
/// <c>&lt;movie&gt;</c>, in the exact shapes section 4 of
/// <see href="https://github.com/prdb-net/prdb-ordeno/blob/main/docs/jellyfin-layout.md">the layout document</see>
/// measured against a real server.
/// </summary>
/// <remarks>
/// <para>
/// Three of those shapes fail silently rather than loudly, which is why they are
/// tested rather than commented:
/// </para>
/// <list type="bullet">
/// <item>
/// <c>&lt;premiered&gt;</c> is parsed against exactly <c>yyyy-MM-dd</c>. An ISO
/// timestamp — what most date libraries hand you by default — is discarded
/// without a word, taking the production year with it.
/// </item>
/// <item>
/// A performer becomes a person only as an <c>&lt;actor&gt;</c> with a
/// <c>&lt;name&gt;</c> child, and <c>&lt;type&gt;</c> must be <c>Actor</c>: text
/// directly inside <c>&lt;actor&gt;</c> is dropped, and a type Jellyfin does not
/// know produces a person of kind <c>Unknown</c> rather than defaulting to an
/// actor.
/// </item>
/// <item>
/// A title is arbitrary text in an XML document. One unescaped <c>&amp;</c>,
/// <c>&lt;</c> or <c>&gt;</c> makes the whole file unparseable, and Jellyfin then
/// uses none of it and falls back to the file name — which looks exactly like a
/// metadata lookup that returned nothing.
/// </item>
/// </list>
/// <para>
/// Nothing here touches a filesystem: the same metadata always produces the same
/// document, which is what lets the writing be tested apart from the replacing.
/// </para>
/// </remarks>
public static class MovieNfo
{
    /// <summary>
    /// What makes a sidecar the tool's own, and the whole of how that question is
    /// answered later. It is a comment rather than an element because Jellyfin's
    /// parser skips comments and this must not become a field the server tries to
    /// read.
    /// </summary>
    /// <remarks>
    /// A user who deletes the line has said the file is theirs, and the tool then
    /// leaves it alone. That is deliberately something they can do with a text
    /// editor and no setting.
    /// </remarks>
    public const string Marker = "Written by prdb-ordeno";

    /// <summary>
    /// The one format <c>&lt;premiered&gt;</c> is parsed against by a default
    /// installation. It is a server setting rather than a constant, which is one
    /// of the three things a configured Jellyfin connection buys (ADR 0018).
    /// </summary>
    private const string ReleaseDateFormat = "yyyy-MM-dd";

    private static readonly string Notice = string.Join(
        '\n',
        string.Empty,
        $"  {Marker}, from what prdb knows about this scene.",
        "  It is written again whenever the tool files this scene, so anything added",
        "  here is lost. Delete this comment and the file is yours: the tool never",
        "  writes over a movie.nfo it cannot find its own name in.",
        "  ");

    /// <summary>The document for one scene, ready to be written as UTF-8.</summary>
    public static string For(SceneMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);

        var document = new Utf8StringWriter();

        using (var writer = XmlWriter.Create(document, Settings))
        {
            writer.WriteComment(Notice);

            writer.WriteStartElement("movie");
            writer.WriteElementString("title", Text(metadata.Title));

            // Bare, and never a timestamp. ADR 0019 leaves the element out
            // entirely where prdb knows no date rather than guessing a year:
            // a wrong date is believed, and an absent one is simply absent.
            if (metadata.ReleaseDate is { } date)
            {
                writer.WriteElementString(
                    "premiered",
                    date.ToString(ReleaseDateFormat, CultureInfo.InvariantCulture));
            }

            if (metadata.Studio is { } studio && Text(studio) is { Length: > 0 } named)
            {
                writer.WriteElementString("studio", named);
            }

            var order = 0;

            foreach (var performer in metadata.Performers)
            {
                if (Text(performer) is not { Length: > 0 } name)
                {
                    // An empty <name> is dropped by Jellyfin anyway, and a person
                    // with no name is not something to have written.
                    continue;
                }

                writer.WriteStartElement("actor");
                writer.WriteElementString("name", name);

                // Whatever prdb calls the role. `Performer` is not a type
                // Jellyfin knows, and an unknown one produces a person filed
                // under nothing rather than an actor.
                writer.WriteElementString("type", "Actor");
                writer.WriteElementString("order", order.ToString(CultureInfo.InvariantCulture));
                writer.WriteEndElement();

                order++;
            }

            // The receipt on the whole document: it comes back as a provider id
            // under the key `prdb`, which is what says the item in the library and
            // the scene in prdb are the same thing.
            writer.WriteStartElement("uniqueid");
            writer.WriteAttributeString("type", "prdb");
            writer.WriteString(metadata.VideoId.ToString("d", CultureInfo.InvariantCulture));
            writer.WriteEndElement();

            writer.WriteEndElement();
        }

        return document.ToString() + '\n';
    }

    /// <summary>
    /// Whether a document on disk is one of these. It is what stands between a
    /// sidecar somebody wrote by hand and a tool that rewrites its own.
    /// </summary>
    public static bool IsOurs(string? document) =>
        document?.Contains(Marker, StringComparison.Ordinal) == true;

    private static XmlWriterSettings Settings => new()
    {
        Indent = true,
        IndentChars = "  ",

        // Written on a NAS and read on whatever the user's desktop is. One line
        // ending, chosen here rather than taken from the machine that happens to
        // be running the container.
        NewLineChars = "\n",

        // Text is put through Text() first, which takes out what XML cannot
        // carry. Leaving this on would turn a control character in a title into
        // an exception thrown after the video has already been moved.
        CheckCharacters = false,
        Encoding = Utf8,
    };

    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>
    /// Arbitrary text as an XML document may carry it. The escaping is
    /// <see cref="XmlWriter"/>'s and is not reimplemented here; what this adds is
    /// the characters no amount of escaping makes valid.
    /// </summary>
    /// <remarks>
    /// A control character is what a scraped title carries when something went
    /// wrong upstream, and it produces a document that parses nowhere — the same
    /// silent failure an unescaped ampersand does. It becomes a space rather than
    /// vanishing, so that two words stay two words.
    /// </remarks>
    private static string Text(string value)
    {
        var kept = new StringBuilder(value.Length);

        foreach (var rune in value.EnumerateRunes())
        {
            if (rune.Value <= char.MaxValue && char.IsControl((char)rune.Value))
            {
                kept.Append(' ');

                continue;
            }

            // What is left of a broken encoding: a lone surrogate arrives here as
            // U+FFFD, and the noncharacters are the other half of what XML 1.0
            // will not take.
            if (rune.Value > char.MaxValue || XmlConvert.IsXmlChar((char)rune.Value))
            {
                kept.Append(rune.ToString());
            }
        }

        return kept.ToString().Trim();
    }

    /// <summary>
    /// A <see cref="StringWriter"/> that says it is UTF-8.
    /// </summary>
    /// <remarks>
    /// <see cref="XmlWriter"/> takes the encoding it declares from the writer it
    /// is given, and a plain <see cref="StringWriter"/> reports UTF-16 — so the
    /// document would announce an encoding it is then not saved in. Nothing about
    /// that is visible until a reader believes the declaration.
    /// </remarks>
    private sealed class Utf8StringWriter() : StringWriter(CultureInfo.InvariantCulture)
    {
        public override Encoding Encoding => Utf8;
    }
}
