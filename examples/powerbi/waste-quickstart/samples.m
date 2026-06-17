// Power Query (M) source for the Samples table — waste only.
//
// Paste this into the Advanced Editor of a new query, OR use it as the source step.
// It pre-filters to the garbage_collection schema at the server so the payload is small.
//
// Requires two Power Query parameters (Home > Manage Parameters > New Parameter, type Text):
//   BaseUrl  e.g. https://ingest.example.org   (no trailing slash)
//   ApiKey   an Operator/Admin key, form keyId.secret
//
// Auth: choose "Anonymous" on the credential dialog — the key is sent as the
// X-Api-Key header set below, not through the dialog.

let
    Source = OData.Feed(
        BaseUrl & "/odata/samples?$filter=SchemaName eq 'garbage_collection'",
        null,
        [
            Implementation = "2.0",
            Headers = [ #"X-Api-Key" = ApiKey ]
        ]
    )
in
    Source
