using Ingest.Core.Entities;

namespace Ingest.Export;

/// <summary>
/// Server-side mirror of the SPA's <c>walkLayout</c> (<c>web/admin/src/utils/layout.ts</c>).
/// Flattens a schema's layout tree into an ordered list of render items — section headers
/// interleaved with the values that sit under them — so a flat Liquid <c>for</c> loop can render
/// the whole document. Unlike the SPA walker this keeps EVERY value and section: the PDF export
/// shows the full schema regardless of <c>visibleIf</c>/<c>enabledIf</c> gating.
/// </summary>
internal static class SchemaLayoutFlattener
{
    /// <summary>Kind discriminator for a flattened section header item.</summary>
    internal const string SectionKind = "section";

    /// <summary>Kind discriminator for a flattened value item.</summary>
    internal const string ValueKind = "value";

    /// <summary>One flattened render item: either a section header or a value row.</summary>
    /// <param name="Kind"><see cref="SectionKind"/> or <see cref="ValueKind"/>.</param>
    /// <param name="Depth">0-based nesting depth (0 = top level).</param>
    /// <param name="Caption">Section caption (section items only).</param>
    /// <param name="Description">Section description (section items only).</param>
    /// <param name="Value">The resolved schema value (value items only).</param>
    internal sealed record LayoutItem(string Kind, int Depth, string? Caption, string? Description, SchemaValue? Value);

    /// <summary>Flatten <paramref name="schema"/>'s layout into an ordered render plan.</summary>
    /// <param name="schema">The schema whose values and layout tree to walk.</param>
    /// <returns>The ordered items: unassigned values first, then the layout tree in order.</returns>
    public static IReadOnlyList<LayoutItem> Flatten(Schema schema)
    {
        var byName = new Dictionary<string, SchemaValue>(StringComparer.OrdinalIgnoreCase);
        foreach (var v in schema.Values) byName[v.Name] = v;

        var referenced = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        CollectReferenced(schema.Layout, referenced);

        var items = new List<LayoutItem>();

        // Values declared on the schema but not placed in the layout render first, under no
        // heading — matching the SPA walker's behaviour.
        foreach (var v in schema.Values)
            if (!referenced.Contains(v.Name))
                items.Add(new LayoutItem(ValueKind, 0, null, null, v));

        Walk(schema.Layout, 0, byName, items);
        return items;
    }

    private static void Walk(
        IEnumerable<SchemaLayoutNode> nodes,
        int depth,
        IReadOnlyDictionary<string, SchemaValue> byName,
        List<LayoutItem> items)
    {
        foreach (var node in nodes)
        {
            if (IsValue(node))
            {
                if (node.ValueName is { } vn && byName.TryGetValue(vn, out var value))
                    items.Add(new LayoutItem(ValueKind, depth, null, null, value));
            }
            else if (IsSection(node))
            {
                items.Add(new LayoutItem(SectionKind, depth, node.Caption, node.Description, null));
                Walk(node.Items, depth + 1, byName, items);
            }
        }
    }

    private static void CollectReferenced(IEnumerable<SchemaLayoutNode> nodes, HashSet<string> into)
    {
        foreach (var node in nodes)
        {
            if (IsValue(node))
            {
                if (node.ValueName is { } vn) into.Add(vn);
            }
            else if (IsSection(node))
            {
                CollectReferenced(node.Items, into);
            }
        }
    }

    private static bool IsValue(SchemaLayoutNode node) =>
        string.Equals(node.Kind, SchemaLayoutNodeKind.Value, StringComparison.OrdinalIgnoreCase);

    private static bool IsSection(SchemaLayoutNode node) =>
        string.Equals(node.Kind, SchemaLayoutNodeKind.Section, StringComparison.OrdinalIgnoreCase);
}
