using Ingest.Core.Entities;
using Microsoft.OData.Edm;
using Microsoft.OData.ModelBuilder;

namespace Ingest.Api.Odata;

/// <summary>
/// Builds the EDM model exposed at <c>/odata</c>. The model only surfaces the denormalised
/// <see cref="SampleProjection"/> entity (as the <c>samples</c> entity set) — that's the only
/// data shape PowerBI is expected to consume directly.
/// </summary>
public static class EdmModelBuilderExtensions
{
    /// <summary>Build the EDM model.</summary>
    /// <returns>An <see cref="IEdmModel"/> ready to be registered with <c>AddOData</c>.</returns>
    public static IEdmModel BuildSamplesEdmModel()
    {
        var builder = new ODataConventionModelBuilder();
        builder.EntitySet<SampleProjection>("samples");
        builder.EnableLowerCamelCase();
        return builder.GetEdmModel();
    }
}
