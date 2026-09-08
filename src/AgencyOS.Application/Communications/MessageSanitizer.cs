using System.Buffers;
using System.Text;

namespace AgencyOS.Application.Communications;

/// <summary>
/// Makes provider HTML safe enough to show.
/// </summary>
/// <remarks>
/// <para>
/// Email HTML is written by strangers and rendered by an application that holds an
/// agency's contracts. The threats are ordinary and well understood: script that
/// runs on the application's origin, an <c>img</c> that tells a sender the moment
/// somebody opened a three-year-old message, a <c>javascript:</c> link, an
/// <c>iframe</c> pointing anywhere at all (ADR-0026).
/// </para>
/// <para>
/// This is an <strong>allow-list</strong>. Everything not named is dropped, which
/// is the only design that stays safe as HTML grows: a deny-list is a list of the
/// attacks somebody had thought of by the time they wrote it.
/// </para>
/// <para>
/// Sanitization happens <strong>on the way in</strong>, once, and the result is
/// what is stored. Sanitizing at render time would mean every future reader had to
/// remember, and one of them would not.
/// </para>
/// </remarks>
public static class MessageSanitizer
{
    /// <summary>Elements whose content survives, stripped of every attribute.</summary>
    /// <remarks>
    /// Structure and emphasis only. No <c>img</c>: an external image in a stored
    /// message is a beacon that fires when somebody browses history, telling the
    /// sender the agency is looking at their email today. No <c>a</c> href either —
    /// the text of a link is kept, so the reader sees what it said, and the
    /// destination does not become clickable inside a privileged window.
    /// </remarks>
    private static readonly HashSet<string> Allowed = new(StringComparer.OrdinalIgnoreCase)
    {
        "p", "br", "div", "span", "b", "strong", "i", "em", "u",
        "ul", "ol", "li", "blockquote", "pre", "code",
        "h1", "h2", "h3", "h4", "h5", "h6",
        "table", "thead", "tbody", "tr", "td", "th", "hr",
    };

    /// <summary>Elements whose <em>content</em> is dropped as well as their tags.</summary>
    /// <remarks>
    /// The text inside a <c>script</c> or a <c>style</c> is not prose, and leaving
    /// it behind would put a page of CSS in the middle of a message.
    /// </remarks>
    private static readonly HashSet<string> Discarded = new(StringComparer.OrdinalIgnoreCase)
    {
        "script", "style", "head", "title", "iframe", "frame", "frameset",
        "object", "embed", "applet", "form", "input", "button", "select",
        "textarea", "svg", "math", "link", "meta", "base", "noscript",
    };

    private static readonly SearchValues<char> TagBoundary = SearchValues.Create("<>");

    /// <summary>
    /// Reduces provider HTML to a small, inert subset.
    /// </summary>
    /// <returns>
    /// Sanitized markup, or null when there was nothing to sanitize. Null is a real
    /// answer: a message with no usable HTML shows its plain-text body instead.
    /// </returns>
    public static string? Sanitize(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return null;
        }

        StringBuilder output = new(html.Length);

        int index = 0;
        int discardDepth = 0;
        string? discarding = null;

        while (index < html.Length)
        {
            int open = html.AsSpan(index).IndexOfAny(TagBoundary);

            if (open < 0)
            {
                AppendText(output, html.AsSpan(index), discardDepth);
                break;
            }

            open += index;

            AppendText(output, html.AsSpan(index, open - index), discardDepth);

            if (html[open] == '>')
            {
                // A stray '>' outside any tag is text, and escaping it is what stops
                // it from closing something later.
                if (discardDepth == 0)
                {
                    output.Append("&gt;");
                }

                index = open + 1;
                continue;
            }

            int close = html.IndexOf('>', open + 1);

            if (close < 0)
            {
                // An unterminated tag. Everything after it is markup nobody can
                // parse, so it is dropped rather than guessed at.
                break;
            }

            ReadOnlySpan<char> tag = html.AsSpan(open + 1, close - open - 1);
            bool closing = tag.Length > 0 && tag[0] == '/';
            string name = ReadName(closing ? tag[1..] : tag);

            if (discarding is not null)
            {
                if (closing && string.Equals(name, discarding, StringComparison.OrdinalIgnoreCase))
                {
                    discardDepth--;

                    if (discardDepth == 0)
                    {
                        discarding = null;
                    }
                }
                else if (!closing
                    && string.Equals(name, discarding, StringComparison.OrdinalIgnoreCase))
                {
                    discardDepth++;
                }
            }
            else if (Discarded.Contains(name))
            {
                // Self-closing forms carry no content to discard.
                if (!closing && !tag.EndsWith("/", StringComparison.Ordinal))
                {
                    discarding = name;
                    discardDepth = 1;
                }
            }
            else if (Allowed.Contains(name))
            {
                // Rewritten from the name alone. Every attribute is dropped, so
                // there is no event handler, no style, no href and no src to audit.
                output.Append('<').Append(closing ? "/" : string.Empty).Append(name).Append('>');
            }

            index = close + 1;
        }

        string sanitized = output.ToString().Trim();

        return sanitized.Length == 0 ? null : sanitized;
    }

    /// <summary>
    /// Produces readable text from HTML, for a message with no plain-text part.
    /// </summary>
    /// <remarks>
    /// Not a rendering engine. It exists so a message that arrived as HTML alone is
    /// still searchable by subject and readable in a list, rather than showing a
    /// row of angle brackets.
    /// </remarks>
    public static string? ToPlainText(string? html)
    {
        string? sanitized = Sanitize(html);

        if (sanitized is null)
        {
            return null;
        }

        StringBuilder text = new(sanitized.Length);
        int index = 0;

        while (index < sanitized.Length)
        {
            int open = sanitized.IndexOf('<', index);

            if (open < 0)
            {
                text.Append(sanitized.AsSpan(index));
                break;
            }

            text.Append(sanitized.AsSpan(index, open - index));

            int close = sanitized.IndexOf('>', open + 1);

            if (close < 0)
            {
                break;
            }

            ReadOnlySpan<char> tag = sanitized.AsSpan(open + 1, close - open - 1);
            string name = ReadName(tag.Length > 0 && tag[0] == '/' ? tag[1..] : tag);

            // Block-level elements become line breaks so paragraphs survive.
            if (name is "p" or "br" or "div" or "tr" or "li"
                or "h1" or "h2" or "h3" or "h4" or "h5" or "h6")
            {
                text.Append('\n');
            }

            index = close + 1;
        }

        string plain = Decode(text.ToString()).Trim();

        return plain.Length == 0 ? null : plain;
    }

    /// <summary>Escapes text so it cannot become markup on the way out.</summary>
    private static void AppendText(StringBuilder output, ReadOnlySpan<char> text, int discardDepth)
    {
        if (discardDepth > 0 || text.Length == 0)
        {
            return;
        }

        foreach (char character in text)
        {
            switch (character)
            {
                case '<':
                    output.Append("&lt;");
                    break;
                case '>':
                    output.Append("&gt;");
                    break;
                case '&':
                    output.Append("&amp;");
                    break;
                default:
                    output.Append(character);
                    break;
            }
        }
    }

    private static string ReadName(ReadOnlySpan<char> tag)
    {
        int length = 0;

        while (length < tag.Length && (char.IsAsciiLetterOrDigit(tag[length]) || tag[length] == '-'))
        {
            length++;
        }

        return new string(tag[..length]);
    }

    /// <summary>Turns the handful of entities this sanitizer produces back into text.</summary>
    private static string Decode(string value) => value
        .Replace("&lt;", "<", StringComparison.Ordinal)
        .Replace("&gt;", ">", StringComparison.Ordinal)
        .Replace("&nbsp;", " ", StringComparison.Ordinal)
        .Replace("&quot;", "\"", StringComparison.Ordinal)
        .Replace("&#39;", "'", StringComparison.Ordinal)
        .Replace("&amp;", "&", StringComparison.Ordinal);
}
