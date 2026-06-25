// Power Query (M) source for the Samples table — schema-agnostic.
//
// Unlike the waste-quickstart this version does NOT filter to one schema, so the
// template works against every schema in your deployment. Add a $filter to the URL
// (see below) if your deployment is large and you only want a subset.
//
// Paste this into the Advanced Editor of a new query named "Samples".
//
// Requires two Power Query parameters (Home > Manage Parameters > New Parameter, type Text):
//   BaseUrl  e.g. https://ingest.example.org   (no trailing slash)
//   ApiKey   an Operator/Admin key, form keyId.secret  (scope it per-department if you want
//            this one file to expose only one directorate's data — see the README)
//
// Auth: choose "Anonymous" on the credential dialog — the key is sent as the
// X-Api-Key header set below, not through the dialog.

let
    Source = OData.Feed(
        BaseUrl & "/odata/samples",
        null,
        [
            Implementation = "2.0",
            Headers = [ #"X-Api-Key" = ApiKey ]
        ]
    ),
    // Flatten the per-type columns (only one is populated per row) into a single Value.
    #"Added Value" = Table.AddColumn(Source, "Value", each
        if [ValueType] = "Number"  then Number.From([NumberValue])  else
        if [ValueType] = "Integer" then Number.From([IntegerValue]) else
        if [ValueType] = "Date"    then DateTime.From([DateValue])  else
        if [ValueType] = "Boolean" then Logical.From([BooleanValue]) else
        [StringValue]),
    // A numeric-only column lets Number and Integer KPIs aggregate together.
    #"Added NumericValue" = Table.AddColumn(#"Added Value", "NumericValue", each
        if [ValueType] = "Number"  then [NumberValue]  else
        if [ValueType] = "Integer" then [IntegerValue] else
        null, type nullable number)
in
    #"Added NumericValue"

// To shrink a large refresh, pre-filter at the server, e.g. last 24 months:
//   BaseUrl & "/odata/samples?$filter=Timestamp ge 2024-06-01T00:00:00Z"
// See docs/setup/powerbi/samples.md § Pre-filtering at the source.
