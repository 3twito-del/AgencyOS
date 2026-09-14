using System.Text.Json;
using System.Text.RegularExpressions;
using System.IO;

namespace AgencyOS.Reviewer.Surface;

/// <summary>One path in the published contract.</summary>
/// <param name="Path">The templated path as OpenAPI writes it.</param>
/// <param name="Methods">HTTP methods the contract declares for it.</param>
/// <param name="Shape">The path with every parameter name erased.</param>
public sealed record ApiPath(string Path, IReadOnlyList<string> Methods, string Shape);

/// <summary>One route the Windows client calls.</summary>
/// <param name="Template">The interpolated template as the client writes it.</param>
/// <param name="Shape">The template with every interpolation erased.</param>
/// <param name="SourceFile">Repository-relative file.</param>
/// <param name="Line">1-based line.</param>
public sealed record ClientRoute(string Template, string Shape, string SourceFile, int Line);

/// <summary>
/// Reconciles the published contract against what the client actually calls.
/// </summary>
/// <remarks>
/// <para>
/// Both sides are reduced to a <em>shape</em>: the path with parameter names
/// erased, so <c>/people/{personId}</c> and <c>/people/{id}</c> compare equal.
/// Comparing names would report a difference in spelling as a missing feature,
/// which is exactly the kind of false finding that makes a gap report ignorable.
/// </para>
/// <para>
/// An endpoint the client does not call is not automatically a defect. It may be
/// administrative, internal, or deliberately server-only. The analyzer says what
/// it found; classification is a judgment recorded in the ledger.
/// </para>
/// </remarks>
public static class ApiScanner
{
    private static readonly Regex RootDefinition =
        new("""private\s+string\s+([A-Za-z0-9_]+Root)\s*=>\s*\$?"([^"]*)"\s*;""", RegexOptions.Compiled);

    // Two alternatives: an interpolated template whose first element is either an
    // interpolation hole or a slash, and a plain literal that already starts with
    // the versioned prefix. Written with escapes rather than as a raw string
    // because the pattern itself is mostly quote characters.
    private static readonly Regex Interpolated = new(
        "\\$\"((?:\\{[A-Za-z0-9_.()\"',: ]+\\}|/)[^\"]*)\"|\"(/api/v1/[^\"]*)\"",
        RegexOptions.Compiled);

    private static readonly Regex Parameter = new(@"\{[^{}]*\}", RegexOptions.Compiled);

    /// <summary>Reads every path from the generated OpenAPI document.</summary>
    public static IReadOnlyList<ApiPath> ReadContract(string documentPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(documentPath);

        using FileStream stream = File.OpenRead(documentPath);
        using JsonDocument document = JsonDocument.Parse(stream);

        List<ApiPath> paths = [];

        if (!document.RootElement.TryGetProperty("paths", out JsonElement pathsElement))
        {
            return paths;
        }

        foreach (JsonProperty path in pathsElement.EnumerateObject())
        {
            List<string> methods = [];

            foreach (JsonProperty operation in path.Value.EnumerateObject())
            {
                if (operation.Name is "get" or "put" or "post" or "delete" or "patch" or "head" or "options")
                {
                    methods.Add(operation.Name.ToUpperInvariant());
                }
            }

            paths.Add(new ApiPath(path.Name, methods, Shape(path.Name)));
        }

        return [.. paths.OrderBy(x => x.Path, StringComparer.Ordinal)];
    }

    /// <summary>Reads every route template the API client builds.</summary>
    public static IReadOnlyList<ClientRoute> ReadClientRoutes(SourceIndex source)
    {
        ArgumentNullException.ThrowIfNull(source);

        Dictionary<string, string> roots = new(StringComparer.Ordinal)
        {
            ["TenantRoot"] = "/api/v1/organizations/{organizationId}",
        };

        List<(string File, string Text)> files =
        [
            .. source.Under("src/AgencyOS.Client/", ".cs").Select(x => (x.Key, x.Value)),
        ];

        // Roots are resolved first, and repeatedly, because one root is defined in
        // terms of another and the partial classes are read in file-name order
        // rather than in definition order.
        for (int pass = 0; pass < 4; pass++)
        {
            foreach ((_, string text) in files)
            {
                foreach (Match match in RootDefinition.Matches(text))
                {
                    roots[match.Groups[1].Value] = Expand(match.Groups[2].Value, roots);
                }
            }
        }

        List<ClientRoute> routes = [];

        foreach ((string file, string text) in files)
        {
            foreach (Match match in Interpolated.Matches(text))
            {
                string raw = match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value;

                if (raw.Length == 0)
                {
                    continue;
                }

                string expanded = Expand(raw, roots);

                if (!expanded.StartsWith("/api/v1/", StringComparison.Ordinal)
                    && !expanded.StartsWith("/version", StringComparison.Ordinal)
                    && !expanded.StartsWith("/health", StringComparison.Ordinal))
                {
                    continue;
                }

                // The root definitions themselves are prefixes, not endpoints. Left
                // in, every root would be reported as a route the contract does not
                // publish, which is true and useless.
                if (roots.Values.Contains(expanded, StringComparer.Ordinal))
                {
                    continue;
                }

                // An interpolation the regex could not close means the literal
                // contained a quote - a formatted identifier, typically - and the
                // captured text is a fragment. A fragment is not a route.
                if (expanded.Count(x => x == '{') != expanded.Count(x => x == '}'))
                {
                    continue;
                }

                // Query strings are appended by the caller and are not part of the
                // path the contract names.
                int query = expanded.IndexOf('?', StringComparison.Ordinal);

                if (query >= 0)
                {
                    expanded = expanded[..query];
                }

                expanded = StripAppendedQuery(expanded);

                routes.Add(new ClientRoute(
                    expanded,
                    Shape(expanded),
                    file,
                    LineOf(text, match.Index)));
            }
        }

        return [.. routes.OrderBy(x => x.Shape, StringComparer.Ordinal).ThenBy(x => x.SourceFile, StringComparer.Ordinal)];
    }

    /// <summary>Erases parameter names so two spellings of one route compare equal.</summary>
    public static string Shape(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        string shaped = Parameter.Replace(path.TrimEnd('/'), "{}");

        return shaped.Length == 0 ? "/" : shaped;
    }

    /// <summary>
    /// Drops an interpolation appended to a path segment rather than forming one.
    /// </summary>
    /// <remarks>
    /// The client writes <c>$"{TenantRoot}/people{query}"</c>, where <c>query</c>
    /// holds a whole query string including its leading question mark. The hole is
    /// part of the segment rather than the segment itself, and that is exactly what
    /// distinguishes it from a route parameter such as <c>/people/{personId}</c>.
    /// </remarks>
    private static string StripAppendedQuery(string path)
    {
        string[] segments = path.Split('/');

        for (int i = 0; i < segments.Length; i++)
        {
            int hole = segments[i].IndexOf('{', StringComparison.Ordinal);

            if (hole > 0)
            {
                segments[i] = segments[i][..hole];

                return string.Join('/', segments[..(i + 1)]);
            }
        }

        return path;
    }

    private static string Expand(string template, IReadOnlyDictionary<string, string> roots)
    {
        string expanded = template;

        foreach ((string name, string value) in roots)
        {
            expanded = expanded.Replace("{" + name + "}", value, StringComparison.Ordinal);
        }

        return expanded;
    }

    private static int LineOf(string text, int index)
    {
        int line = 1;

        for (int i = 0; i < index && i < text.Length; i++)
        {
            if (text[i] == '\n')
            {
                line++;
            }
        }

        return line;
    }
}
