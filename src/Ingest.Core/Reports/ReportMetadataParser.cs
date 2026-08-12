using Ingest.Core.Common;
using Ingest.Core.Entities;

namespace Ingest.Core.Reports;

/// <summary>
/// Metadata extracted from the YAML front matter at the top of a report document, plus the
/// front-matter-stripped template body.
/// </summary>
/// <param name="Name">Optional explicit report name. When absent the caller uses the file name to derive one.</param>
/// <param name="Label">Optional friendly label.</param>
/// <param name="Description">Optional description.</param>
/// <param name="Type">Data envelope the template expects. Defaults to <see cref="ReportType.Aggregate"/> when omitted.</param>
/// <param name="TargetSchemaNames">Schemas the report applies to (empty list ⇒ global).</param>
/// <param name="Template">The document body with the front matter (and the fences) removed; equal to the original text when no front matter was present.</param>
public sealed record ReportMetadata(
    string? Name,
    string? Label,
    string? Description,
    ReportType? Type,
    IReadOnlyList<string> TargetSchemaNames,
    string Template);

/// <summary>
/// Minimal YAML front-matter parser tailored to the small fixed schema reports use. Avoids
/// pulling in a full YAML library (we only support the four whitelisted keys below) and keeps
/// the surface easy to test and easy to fuzz.
/// </summary>
/// <remarks>
/// Recognised keys:
/// <list type="bullet">
/// <item><description><c>name</c> — string, machine-style identifier.</description></item>
/// <item><description><c>label</c> — string, friendly title.</description></item>
/// <item><description><c>description</c> — string, free-form description.</description></item>
/// <item><description><c>type</c> — <c>Single</c> | <c>Aggregate</c> (case-insensitive).</description></item>
/// <item><description><c>schemas</c> — list, accepts both inline (<c>[a, b]</c>) and the <c>- a</c> block form.</description></item>
/// </list>
/// Everything else is silently ignored so adding future fields to a template won't break the
/// parser. Quoted (single/double) and unquoted scalars are both accepted; quotes are stripped.
/// </remarks>
public static class ReportMetadataParser
{
    /// <summary>Parse the front matter (if any) from the supplied document and return both the metadata and the stripped template body.</summary>
    /// <param name="content">Full report document.</param>
    /// <returns>The extracted metadata + template.</returns>
    /// <exception cref="ValidationException">The front matter opens but never closes, or contains unparseable syntax.</exception>
    public static ReportMetadata Parse(string content)
    {
        if (string.IsNullOrEmpty(content))
            return new ReportMetadata(null, null, null, null, Array.Empty<string>(), string.Empty);

        // Front matter only counts when the document literally opens with --- on its own line.
        // CRLF and LF are both fine — we normalise the search on the first newline.
        if (!StartsWithFence(content, out var firstFenceEnd))
            return new ReportMetadata(null, null, null, null, Array.Empty<string>(), content);

        // Look for the closing fence: a line that is exactly "---" (after the leading newline of
        // the previous line). We accept both `\n---\n` and `\n---\r\n` and trailing whitespace
        // on the fence line itself.
        var closeIdx = FindClosingFence(content, firstFenceEnd);
        if (closeIdx < 0)
            throw new ValidationException(new[]
            {
                new Diagnostic(
                    DiagnosticCodes.Reports.FrontMatterUnclosed,
                    "Report front matter opens with '---' but never closes."),
            });

        var fmBody = content.Substring(firstFenceEnd, closeIdx - firstFenceEnd);
        var template = content.Substring(closeIdx + ClosingFenceLength(content, closeIdx));
        // Strip a single leading newline so the template doesn't start with a blank line.
        if (template.StartsWith("\r\n", StringComparison.Ordinal)) template = template.Substring(2);
        else if (template.StartsWith("\n", StringComparison.Ordinal)) template = template.Substring(1);

        var (name, label, description, type, schemas) = ParseFrontMatter(fmBody);
        return new ReportMetadata(name, label, description, type, schemas, template);
    }

    private static bool StartsWithFence(string content, out int afterFenceIdx)
    {
        afterFenceIdx = 0;
        // We need the first line (trimmed) to equal "---".
        var nl = content.IndexOf('\n');
        if (nl < 0) return false;
        var firstLine = content.Substring(0, nl).TrimEnd('\r').Trim();
        if (firstLine != "---") return false;
        afterFenceIdx = nl + 1;
        return true;
    }

    private static int FindClosingFence(string content, int startIdx)
    {
        var idx = startIdx;
        while (idx < content.Length)
        {
            var nl = content.IndexOf('\n', idx);
            var lineEnd = nl < 0 ? content.Length : nl;
            var line = content.Substring(idx, lineEnd - idx).TrimEnd('\r').Trim();
            if (line == "---") return idx;
            if (nl < 0) return -1;
            idx = nl + 1;
        }
        return -1;
    }

    private static int ClosingFenceLength(string content, int fenceLineStart)
    {
        var nl = content.IndexOf('\n', fenceLineStart);
        if (nl < 0) return content.Length - fenceLineStart;
        return nl - fenceLineStart + 1;
    }

    private static (string? name, string? label, string? description, ReportType? type, IReadOnlyList<string> schemas)
        ParseFrontMatter(string fmBody)
    {
        string? name = null, label = null, description = null;
        ReportType? type = null;
        List<string>? schemas = null;

        // Split into logical lines. We don't support nested mappings (only the whitelisted top-
        // level keys) so a simple line scan with a peek-ahead for `- value` blocks under
        // `schemas:` is enough.
        var lines = fmBody.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var errors = new List<Diagnostic>();

        for (var i = 0; i < lines.Length; i++)
        {
            var raw = lines[i];
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            // Inline list values get captured by ParseScalarOrInlineList below.
            var colon = line.IndexOf(':');
            if (colon <= 0)
            {
                errors.Add(Diagnostic.Create(
                    DiagnosticCodes.Reports.FrontMatterLineInvalid,
                    $"Cannot parse front-matter line: '{raw}'.",
                    ("line", raw),
                    ("lineNumber", i + 1)));
                continue;
            }
            var key = line.Substring(0, colon).Trim().ToLowerInvariant();
            var rest = line.Substring(colon + 1).Trim();

            switch (key)
            {
                case "name":
                    name = StripQuotes(rest);
                    break;
                case "label":
                    label = StripQuotes(rest);
                    break;
                case "description":
                    description = StripQuotes(rest);
                    break;
                case "type":
                    var v = StripQuotes(rest);
                    if (string.IsNullOrEmpty(v)) break;
                    if (!Enum.TryParse<ReportType>(v, ignoreCase: true, out var parsed))
                    {
                        errors.Add(Diagnostic.Create(
                            DiagnosticCodes.Reports.FrontMatterTypeInvalid,
                            $"Front-matter 'type' must be 'Single' or 'Aggregate' (got '{v}').",
                            ("actualType", v),
                            ("allowedTypes", new[] { "Single", "Aggregate" })));
                        break;
                    }
                    type = parsed;
                    break;
                case "schemas":
                    schemas = new List<string>();
                    if (rest.Length > 0)
                    {
                        // Inline form: schemas: [a, b]
                        foreach (var s in ParseInlineList(rest, errors)) schemas.Add(s);
                    }
                    else
                    {
                        // Block form: scan following indented "- value" lines.
                        while (i + 1 < lines.Length)
                        {
                            var next = lines[i + 1];
                            var trimmed = next.TrimStart();
                            if (trimmed.StartsWith("- ", StringComparison.Ordinal) || trimmed == "-")
                            {
                                var v2 = trimmed.Length >= 2 ? StripQuotes(trimmed.Substring(2).Trim()) : string.Empty;
                                if (!string.IsNullOrEmpty(v2)) schemas.Add(v2);
                                i++;
                            }
                            else if (next.Length == 0 || next.StartsWith(' ') || next.StartsWith('\t'))
                            {
                                // Skip blank/indented non-list lines (tolerate stray comments).
                                if (next.Trim().StartsWith('#')) { i++; continue; }
                                break;
                            }
                            else break;
                        }
                    }
                    break;
                default:
                    // Unknown keys: ignore so the format can be extended later without breaking
                    // older deployments.
                    break;
            }
        }

        if (errors.Count > 0)
            throw new ValidationException(errors);
        return (name, label, description, type, (IReadOnlyList<string>)(schemas ?? new List<string>()));
    }

    private static string StripQuotes(string raw)
    {
        if (raw.Length >= 2 &&
            ((raw[0] == '"' && raw[^1] == '"') || (raw[0] == '\'' && raw[^1] == '\'')))
        {
            return raw.Substring(1, raw.Length - 2);
        }
        return raw;
    }

    private static IEnumerable<string> ParseInlineList(string raw, List<Diagnostic> errors)
    {
        var s = raw.Trim();
        if (s.Length < 2 || s[0] != '[' || s[^1] != ']')
        {
            errors.Add(Diagnostic.Create(
                DiagnosticCodes.Reports.FrontMatterInlineListInvalid,
                $"Inline list must be wrapped in [..] (got '{raw}').",
                ("value", raw),
                ("expectedWrapper", "[..]")));
            yield break;
        }
        var inner = s.Substring(1, s.Length - 2);
        foreach (var part in inner.Split(','))
        {
            var v = StripQuotes(part.Trim());
            if (v.Length > 0) yield return v;
        }
    }
}
