// Power Query (M) source for the Schemas dimension — labels, units, types and RAG bands.
//
// Optional but recommended: it gives every visual a friendly label and unit without
// hard-coding, and carries the target-band edges so you can colour against them.
// Paste into the Advanced Editor of a new query named "Schemas".
//
// Same two parameters and the same Anonymous + X-Api-Key recipe as samples.m.
// Relate Schemas to Samples on BOTH SchemaName and ValueName (a composite key, or
// a calculated key column on each side: SchemaName & "|" & ValueName).

let
    Source = OData.Feed(
        BaseUrl & "/odata/schemas",
        null,
        [
            Implementation = "2.0",
            Headers = [ #"X-Api-Key" = ApiKey ]
        ]
    ),
    // Each schema row nests its Values; expand them to one row per (schema, value).
    #"Expanded Values" = Table.ExpandTableColumn(
        Source, "Values",
        {"Name", "Label", "Unit", "Type", "Cadence", "Required",
         "GreenMin", "GreenMax", "AmberMin", "AmberMax"},
        {"ValueName", "ValueLabel", "Unit", "ValueType", "Cadence", "Required",
         "GreenMin", "GreenMax", "AmberMin", "AmberMax"}
    ),
    // Composite key to relate onto Samples (Power BI single-column relationships).
    #"Added Key" = Table.AddColumn(#"Expanded Values", "Key",
        each [SchemaName] & "|" & [ValueName], type text)
in
    #"Added Key"

// If your /odata/schemas value shape differs (field names/casing), adjust the
// column list in #"Expanded Values" to match — see docs/setup/powerbi/schemas.md.
