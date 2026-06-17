// Add Column > Custom Column. Name it "Value".
//
// Collapses the per-type columns (only one is populated per row) into a single value
// for slicers and tables. Keep the original *Value columns too — the numeric measures
// in measures.dax aggregate NumberValue / IntegerValue directly.

if [ValueType] = "Number"  then Number.From([NumberValue])  else
if [ValueType] = "Integer" then Number.From([IntegerValue]) else
if [ValueType] = "Date"    then DateTime.From([DateValue])  else
if [ValueType] = "Boolean" then Logical.From([BooleanValue]) else
[StringValue]
