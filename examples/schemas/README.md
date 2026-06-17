# Example schemas

Ready-made schema definitions (KPI packages) you can upload to an Ingest deployment **without changing the product code**. An administrator uploads one via **Schemas → New schema → Upload JSON…** in the admin console (or `POST /api/admin/schemas`), then services submit samples against it. Use them as-is or as a starting point and tweak the values, cadences and validation rules to fit your service.

| File | Schema name | What it covers |
|------|-------------|----------------|
| [garbage-collection.json](garbage-collection.json) | `garbage_collection` | Daily kerbside-collection operations — tonnage, routes, fleet incidents, recycling performance — with mixed cadences and conditional (`visibleIf`) fields, plus a monthly compliance checkpoint. |
| [generic.json](generic.json) | `weekly_workforce` | A lightweight weekly headcount/availability snapshot (active staff, sick leave, contractors, overtime) that any department can adopt as a starter. |
| [finance-monthly-close.json](finance-monthly-close.json) | `finance_monthly_close` | A monthly finance close — budget vs actual with variance, revenue vs target, invoice/reconciliation checks — showing cross-value validation rules. |

These are the schemas the [example integrations](../integrations/README.md) submit against and the [example reports](../reports/README.md) render, so uploading them lets you try the whole flow end to end.

## See also

- [docs/admin-user-guide/schemas.md](../../docs/admin-user-guide/schemas.md) — authoring and uploading schemas, validation rules, versioning.
- [examples index](../README.md)
