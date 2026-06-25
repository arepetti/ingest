// Power Query (M) for a Calendar table — needed for time-intelligence
// (YTD, same-period-last-year, rolling averages).
//
// Paste into the Advanced Editor of a new query named "Calendar".
// Then in Model view relate Calendar[Date] -> Samples[Timestamp] (single direction,
// one-to-many) and mark Calendar as the date table (Table tools > Mark as date table).
//
// It derives its range from the Samples query, so it always covers your data.

let
    MinDate = Date.From( List.Min( Samples[Timestamp] ) ),
    MaxDate = Date.From( List.Max( Samples[Timestamp] ) ),
    DayCount = Duration.Days( MaxDate - MinDate ) + 1,
    Dates = List.Dates( MinDate, DayCount, #duration(1, 0, 0, 0) ),
    Table = Table.FromList( Dates, Splitter.SplitByNothing(), {"Date"} ),
    Typed = Table.TransformColumnTypes( Table, {{"Date", type date}} ),
    Cols = Table.AddColumn( Typed, "Year",    each Date.Year([Date]),        Int64.Type ),
    Cols2 = Table.AddColumn( Cols, "Month",    each Date.Month([Date]),       Int64.Type ),
    Cols3 = Table.AddColumn( Cols2, "MonthName", each Date.MonthName([Date]), type text ),
    Cols4 = Table.AddColumn( Cols3, "Quarter",  each "Q" & Text.From(Date.QuarterOfYear([Date])), type text ),
    Cols5 = Table.AddColumn( Cols4, "YearMonth", each Date.ToText([Date], "yyyy-MM"), type text )
in
    Cols5
