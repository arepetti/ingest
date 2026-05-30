# Troubleshooting

The most common things admins run into, with the fix.

**I can sign in, but every page shows "Forbidden".**
You probably have an `Operator` key when you need an `Admin` key (or vice versa). Sign in with an Admin key for write operations on accounts/schemas; Operator keys are read-only outside the service-facing endpoints. See [../architecture/authentication.md § Roles](../architecture/authentication.md#roles).

**My new key doesn't authenticate even though I copied it correctly.**
The `Account` may be disabled or soft-deleted. Reopen the account (set `includeDeleted=true` on the API or use Mongo directly) and check `enabled`/`isDeleted`. If the account is fine, double-check the `ApiKey:Pepper` value matches between deployments — rotating the pepper invalidates every key.

**Submissions get accepted but don't show in PowerBI.**
The OData feed reads `SampleProjection` rows, which are rebuilt **per submission save**. If a save crashed mid-way, the projection can drift; deleting and re-creating the submission rebuilds the projection rows. If you see persistent drift, the indexes on the `samples` collection are the place to look (see [../architecture/architecture.md § Mongo indexes](../architecture/architecture.md#mongo-indexes)).

**Cadence error: "already submitted for this period".**
That value already has a sample in the current cadence bucket for the same service. To replace it, use `PUT /api/submissions/{id}` on the existing one (within the cadence window) or have an admin do it via the on-behalf-of edit form ([submissions.md](submissions.md)).

**The submission editor shows fields that should be hidden.**
The `Enabled if` / `Visible if` evaluation needs the server-side translation of the rule to finish (a one-shot per unique rule, cached for the lifetime of the page). Until it arrives the editor stays permissive — every field is shown — so a broken rule never makes inputs disappear silently. If a particular rule never resolves, check the browser console for a 4xx from `POST /api/expressions/translate`; the response detail will tell you what the parser thinks is wrong with the expression.

**The server returns warnings I didn't expect.**
A value's `Enabled if` / `Visible if` rule evaluated to false in the context of the rest of the submission, so the system dropped the sample on purpose. Look at the warning text (it includes the rule that fired) and the other values you sent. See [validation.md § Conditional display](validation.md#conditional-display-enabled-if--visible-if) for the full semantics.

**A schema's value renamed to `null` everywhere.**
Schema-level rules and conditional-display rules reference values by **name** (the machine-style identifier), not label. Renaming a value silently turns its reference inside every rule into `null`. Search the rules in this schema for the old name and update them — the validation editor doesn't fix references for you. See [validation.md § Troubleshooting](validation.md#troubleshooting) for more rule-authoring pitfalls.

**Where can I see what the server actually rejected?**
The full validation guide ([validation.md § Troubleshooting](validation.md#troubleshooting)) lists every error message the validator can emit, alongside what causes it.

## Where to go next

- [validation.md](validation.md) — the rule-authoring reference; its own troubleshooting section is worth a skim if you maintain schemas.
- [../client/api.md](../client/api.md) — the API surface a service calls; useful for telling clients what status code they should expect for a given situation.
- [../architecture/authentication.md](../architecture/authentication.md) — when an auth issue isn't explained by the table above.
