# Submission approval

The approval workflow lets you hold submissions for review before they go "live" — that is, before they appear in the OData feed, the Explore page, reports, and webhooks. It is **optional, off-per-schema by default, and back-compatible**: with no policy configured nothing changes, so a deployment that never touches it behaves exactly as it always did.

> **Master switch.** The whole feature is gated by `Approval:Enabled` (default `true`; see [setup/configuration.md § Approval workflow](../setup/configuration.md#approval-workflow)). When it's off, none of the UI or endpoints described here exist. When it's on but no schema requires approval, it's still invisible in practice — submissions simply flow straight through.

## The lifecycle

Every submission carries an **approval status**:

| Status        | Meaning                                                                                  | In reporting? |
|---------------|------------------------------------------------------------------------------------------|---------------|
| `NotRequired` | Approval didn't apply (the default for everything when no policy gates the submission).  | Yes (live)    |
| `Pending`     | Awaiting review. Visible in the console, **excluded** from the OData feed and Explore.   | No            |
| `Approved`    | Every required approver signed off. Behaves exactly like `NotRequired`.                  | Yes (live)    |
| `Rejected`    | A reviewer rejected it. Stays visible (with the reason) but is **excluded** from reporting. | No          |

A `Pending` or `Rejected` submission is held out of the live projection, so Power BI, the OData feed, Explore and reports never see it. It's still fully visible on the **Submissions** page so submitters and reviewers can see what's happening.

> **Drafts never enter approval.** A submission [saved as a draft](submissions.md#saving-a-draft) sits outside this lifecycle entirely — it's `NotRequired` but flagged as a draft, so it has no required approvers and is held out of reporting like a `Pending` one. The approval policy is resolved only when the draft is **published**, at which point it lands in `Pending` (or goes live) exactly as a first-time submission would.

## Who can approve

Approving or rejecting needs the **`submissions:approve`** capability (and `submissions:read` to see the queue). The **Approver** role is just the template that seeds exactly those two capabilities; an **Admin** holds every capability and can always approve. You can equally grant `submissions:approve` to any other non-admin account that should be able to review. See [accounts.md § Permissions (capabilities)](accounts.md#permissions-capabilities) and [architecture/authentication.md § Authorisation: capabilities](../architecture/authentication.md#authorisation-capabilities).

> If a reviewer has a [service scope](accounts.md#service-scope-limiting-an-operator-to-a-subset-of-services), their review queue (and the dashboard pending count) only ever shows submissions from their assigned services — out-of-scope submissions are invisible to them, and they cannot approve or reject them.

A policy names its approvers, each marked **Required** or **Optional**. An approver can be either:

- a specific **approver account** (any account that holds `submissions:approve` — Approver- or Admin-role accounts by default), or
- the **service owner** — a dynamic approver that resolves, per submission, to the account that *sent* it. This lets a service sign off on its own data: the same account (or API key) submits and then approves as a required extra step.

Then:

- A submission goes live once **every Required approver** has approved it. Optional approvers may also approve (their decision is recorded) but they don't gate the transition.
- An **Admin** can approve any pending submission outright, even if they aren't a named approver.

> The same account that submitted data may also approve it — approval is just a required extra step, not a separation-of-duties control. The **service owner** approver makes this explicit; if instead you need two distinct sign-offs, name two Required approver accounts.

### Common approver recipes

Two patterns cover most "the service signs off on its own data" needs:

- **Service self-approval (light-touch automation).** Name the **service owner** as the single Required approver. The same account (or API key) that submits then approves — a deliberate extra step that catches an obviously-wrong automated push before it goes live, without involving anyone else. Pair it with a [source-scoped rule](#rules-per-service--schema) set to *API submissions only* to gate just the automated feed.
- **Service-manager approval (separation of duties).** Create a dedicated **Approver-role account** for the manager, give it a [service scope](accounts.md#service-scope-limiting-an-operator-to-a-subset-of-services) covering only the services they manage, and grant it just `submissions:read` + `submissions:approve`. Name that account as the Required approver. Now the person who enters the data and the person who signs it off are different, the manager only ever sees their own services' queue, and they can't touch anything else in the system.

## Choosing what needs approval

A policy is **source-aware**: you can require approval for manual (web-console) entries only, API submissions only, or both. This lets you, for example, trust an automated integration but review everything a human types in by hand — or vice-versa.

### Per-schema policy

Open **Schemas → (edit a schema) → Approval**. Pick a **mode**:

- **No approval required** — the schema's submissions are never gated (the default).
- **Use the global default** — defer to the server-wide default policy (configured in Settings, below).
- **Approval required** — gate this schema. Then choose the **source scope** (both / manual only / API only) and the **approvers** (mark at least one Required). The approver picker includes the **service owner** alongside every account that holds `submissions:approve`.

### Global default

Open **Settings → Approval** (needs `settings:read` to view, `settings:manage` to change — both in the Admin default bundle). The global default is the policy schemas fall back to when they're set to **Use the global default**. It can be "no approval" or "approval required" with its own source scope and approver list. Changing it affects only **new** submissions — in-flight ones keep the approvers they were created with (the policy is snapshotted onto each submission when it first becomes `Pending`).

### Rules (per service + schema)

Open **Settings → Rules** (same `settings:read` / `settings:manage` capabilities as the global default). A **rule** requires approval for a chosen set of **services** and **schemas**, regardless of what those schemas' own policies say. It's the answer to "service A submitting schema B needs sign-off" without having to gate schema B for everyone.

- **Either side can be "All".** Tick **All services** to mean "every service", and/or **All schemas** to mean "every schema". A rule with both set to "All" requires approval for everything (even if that duplicates a schema- or global-level policy — rules are additive, never subtractive).
- **Multiple selections.** Pick several services and several schemas in one rule; it applies to every combination of the two.
- **Each rule carries its own policy.** Choose **Required** (with its own source scope and approver list, including the service owner) or **Use the global default**. Mark at least one approver as Required, exactly like the other editors.
- **Additive resolution.** A submission needs approval if its schema/global policy requires it **or** any enabled matching rule does. When more than one applies, their approvers are merged (a Required approver always wins over the same account listed as Optional).
- Rules are listed in a table with the usual row menu (**Edit** / **Delete**); click a row to edit it in the side drawer. Disabled rules are kept but ignored.

> **Forcing manual intervention for automated feeds.** Set a rule's **source scope** to *API submissions only* to hold an automated integration's data as `Pending` while a person reviews it. This is useful for **partially automated** feeds — where a script can post most of a schema but some values need a human to check or fill them in — because the held submission can be edited (via on-behalf-of) and approved before it goes live. Direct API feeds you fully trust are unaffected; only the services/schemas the rule names are gated.

Like the global default, changing a rule affects only **new** submissions; in-flight ones keep the approvers they were snapshotted with.

## Reviewing submissions

- **Dashboard.** Accounts with `submissions:approve` get a **Pending approvals** card showing the count, with a **Review** button that jumps to the Submissions page filtered to `Pending`.
- **Submissions page.** A **Status** column shows each submission's approval state, and an **Approval** filter narrows the list (e.g. to the pending queue). Each pending row has quick **Approve** (✓) and **Reject** (✕) actions right before the row menu; the read-only drawer shows the same actions plus the approver progress and any recorded decisions.
- **Rejecting.** Rejecting opens a dialog for an optional **reason**. The reason is visible to the submitter and to other reviewers, both in the Status-column tooltip and in the submission drawer.
- **Audit.** Approve and Reject decisions are recorded in the [audit log](../admin-user-guide/README.md) as `Approve` / `Reject` entries, with the reason captured in the entry's **Note** field.

## Notifications and webhooks

The workflow can announce each transition over email and/or outbound webhooks. Both are optional and off by default.

- **Email.** When email is enabled, the [Notifications settings](settings.md#notifications) gain three approval triggers: **pending approval**, **approved**, and **rejected**. The pending notice always emails the submission's designated approvers (so they know there's something to review); each trigger can additionally copy the submitter and/or the admin recipient list. These notices are **event-driven** — sent the instant the submission changes state, not on the notification timer. Edit the wording in **Email templates** (`notification.pendingApproval`, `notification.approved`, `notification.rejected`).
- **Webhooks.** When webhooks are enabled, endpoints can subscribe to `submission.pending_approval`, `submission.approved`, and `submission.rejected` (see [webhooks.md](webhooks.md)). The rejected payload carries the reviewer's `note`. `submission.accepted` continues to fire on the approve transition too, because the submission becomes live at that point.

## Re-submitting and the replace-and-reset rule

Submitting data for a window that already has a submission **replaces** it. When approval applies, the replacement **resets the approval status back to `Pending`** — even if the previous submission was already approved and live:

- Re-sending data for a still-pending window updates it and keeps it pending.
- Re-sending after a rejection clears the rejection and starts a fresh review.
- **Editing an already-approved submission removes it from reporting until it's approved again.** While it waits for re-approval it drops out of the OData feed and Explore.

> **Caution — modifiable schemas.** If a schema is both **modifiable** and gated by approval, a re-submission can knock previously-approved data out of live reporting until someone re-approves it. The schema editor shows this warning inline. If you don't want re-submissions to disturb approved data, mark the schema as **not modifiable** — then a window can't be re-submitted once it's in.

## API behaviour

The approval workflow is transparent to API callers — they submit exactly as before (see [client/api.md](../client/api.md)). What changes:

- Submissions are tagged with a **source** so source-aware policies can apply. Direct API calls default to `Api`; the admin console tags its writes as `Manual` (via the `X-Ingest-Source` header).
- A submission that needs approval is accepted and stored as normal, but stays out of the OData feed until approved. The create/replace response is unchanged.
- An [approval **rule**](#rules-per-service--schema) scoped to API submissions can deliberately hold an automated feed for review — letting a person complete or sign off on a partially automated submission before it goes live — without the integration code changing at all.
- Approve/reject are admin/approver actions (`POST /api/admin/submissions/{id}/approve` and `.../reject`, optional `{ "note": "…" }` body).

## See also

- [submissions.md](submissions.md) — the Submissions page, filters, and on-behalf-of entry.
- [schemas.md](schemas.md) — designing schemas, including the per-schema approval policy.
- [settings.md](settings.md) — the global default approval policy.
- [setup/configuration.md § Approval workflow](../setup/configuration.md#approval-workflow) — the master switch.
