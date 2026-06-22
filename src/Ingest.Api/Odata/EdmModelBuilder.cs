using Ingest.Core.Entities;
using Microsoft.OData.Edm;
using Microsoft.OData.ModelBuilder;

namespace Ingest.Api.Odata;

/// <summary>
/// Builds the EDM model exposed at <c>/odata</c>. It surfaces the denormalised
/// <see cref="SampleProjection"/> entity (as the <c>samples</c> entity set), a simplified
/// <see cref="SchemaSummary"/> catalogue (as the <c>schemas</c> entity set), plus an unbound
/// <c>scorecard(mode,period)</c> function that returns a flat, banded RAG status board
/// (<see cref="ScorecardCard"/>) — the shapes PowerBI is expected to consume directly.
/// </summary>
public static class EdmModelBuilderExtensions
{
    /// <summary>Build the EDM model.</summary>
    /// <returns>An <see cref="IEdmModel"/> ready to be registered with <c>AddOData</c>.</returns>
    public static IEdmModel BuildEdmModel()
    {
        var builder = new ODataConventionModelBuilder();
        builder.EntitySet<SampleProjection>("samples");

        // Simplified schema catalogue. SchemaSummary has no Id, so key off the unique machine name;
        // SchemaValueSummary stays keyless and is inferred as a nested complex type.
        builder.EntitySet<SchemaSummary>("schemas").EntityType.HasKey(s => s.Name);

        // Flat scorecard cards. The entity set has no GET handler of its own (the data is computed,
        // not stored); it exists so the function below can return from it and so $select/$filter/
        // $orderby work against the card's properties.
        builder.EntitySet<ScorecardCard>("scorecardCards");

        // Unbound function: scorecard(mode='LatestAvailable',period='Current'). String params keep
        // the URL PowerBI-friendly (no namespace-qualified enum literals); the controller parses
        // them leniently. Mirrors the Explore page's Show/Period selectors.
        var scorecard = builder.Function("scorecard");
        scorecard.Parameter<string>("mode");
        scorecard.Parameter<string>("period");
        scorecard.ReturnsCollectionFromEntitySet<ScorecardCard>("scorecardCards");

        // Property names are kept PascalCase (the CLR names) so the serialized columns match the
        // documented feed reference and the shipped Power BI examples exactly (e.g. SchemaName,
        // NumberValue). Do NOT enable lower-camel-case here — it would silently rename every wire
        // column and break those recipes.
        return builder.GetEdmModel();
    }
}
