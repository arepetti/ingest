using Ingest.Core.Entities;
using Microsoft.OData.Edm;
using Microsoft.OData.ModelBuilder;

namespace Ingest.Api.Odata;

/// <summary>
/// Builds the EDM model exposed at <c>/odata</c>. It surfaces the denormalised
/// <see cref="SampleProjection"/> entity (as the <c>samples</c> entity set) plus an unbound
/// <c>scorecard(mode,period)</c> function that returns a flat, banded RAG status board
/// (<see cref="ScorecardCard"/>) — the two shapes PowerBI is expected to consume directly.
/// </summary>
public static class EdmModelBuilderExtensions
{
    /// <summary>Build the EDM model.</summary>
    /// <returns>An <see cref="IEdmModel"/> ready to be registered with <c>AddOData</c>.</returns>
    public static IEdmModel BuildSamplesEdmModel()
    {
        var builder = new ODataConventionModelBuilder();
        builder.EntitySet<SampleProjection>("samples");

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

        builder.EnableLowerCamelCase();
        return builder.GetEdmModel();
    }
}
