# Ingest — an IT manager's honest review

*Context: I'm the IT manager in a local council. My data analysts build Power BI dashboards to track KPIs (e.g. four-days-per-week efficiency metrics). Today the data arrives via Excel workbooks, manually filled by each service, linked to SharePoint lists, which Power BI then consumes. This is my evaluation of whether the Ingest application should replace that.*

---

## 1. The current setup, and every pain point baked into it

Each service (waste, roads, public health, finance…) keeps an Excel workbook, those workbooks are linked to SharePoint lists, and Power BI pulls from there. Reconstructing how that's actually wired, the pain points fall straight out of the implementation.

### Data quality / validation
- Excel does almost no real validation. People paste text into number cells, break formulas, leave required cells blank, type "12.5k" or "N/A", use the wrong units (litres vs m³), and enter dates in three different formats.
- SharePoint list column typing is loose and easily bypassed (especially when fed from a linked sheet). Garbage reaches the list, then Power BI.
- No cross-field business rules that survive. An Excel formula for "expenses ≤ revenue" gets deleted or overwritten by the next person.
- Analysts inherit dirty data and spend a chunk of every reporting cycle cleaning it. The dashboard is only as trustworthy as the worst-filled cell.

### Control over submissions
- No cadence enforcement. Nothing stops a service entering the same week twice, skipping a week, or back-dating. Duplicates, gaps, and "which row is the real one?"
- No concept of "one official submission per period." Multiple people edit the same sheet; last-write-wins; conflicts and "Copy of Copy of KPIs_final_v3.xlsx".
- No locking of a period once closed. Numbers silently change after the dashboard was already reported upstream.
- No visibility into who hasn't submitted. Chasing laggards is a manual email/Teams ritual every cycle.

### Authorization / access control
- SharePoint permissions are a genuine tarbaby: per-site, per-list, per-item, broken inheritance, "share" links that proliferate, and the inevitable "shared with Everyone" accident.
- Hard to scope a service to *only its own data*. Hard to cleanly revoke when someone leaves (orphaned access lingers).
- Contractor / cross-department access is fiddly and rarely cleaned up.

### Storage & security
- SharePoint is a document store, not a controlled data store. Files get downloaded, emailed, copied to laptops, kept in personal OneDrive. No real control over the spread.
- No meaningful audit of *reads*, weak audit of edits, and no enforced retention. Personal data in free-text cells is a latent GDPR liability with no erasure or DSAR story.
- No schema/catalogue: no central, enforced definition of what each KPI is, its type, unit, or due date. Every workbook drifts independently.

### Reporting fragility (the Power BI tax)
- Power BI bound to Excel/SharePoint columns is brittle: rename a column, move a file, lock a workbook, or change a sheet name and the refresh breaks.
- Gateway and credential headaches; data-type inference surprises; schema drift silently corrupting visuals.

### Effort, automation & governance
- Pure manual labour: numbers that already live in finance/ERP/scheduling systems get re-typed by hand every week — slow, error-prone, dependent on someone remembering before the window closes.
- No way for source systems to push data automatically.
- No alerting on missed/late submissions. No audit trail of corrections. No structured governance as the number of services grows — just sprawl.

**Short version:** no validation, no submission control, painful authz, insecure storage, no catalogue, brittle reporting, all-manual, no audit/GDPR.

---

## 2. Does Ingest solve my problems?

Mapping the app against the list above — backed by the actual code, not just the README:

- **Validation** → Solved well. Server-side shape checks (type, min/max, length, regex) plus an NCalc expression engine for cross-field business rules, conditional fields, and non-blocking "soft warnings." A real ~550-line validation pipeline (`SubmissionValidator.cs`). Data is clean *before* it lands.
- **Submission control / cadence** → Solved. Seven cadences, at most one submission per period per service per KPI, silent duplicates rejected. Exactly the "one official figure per window" control missing today.
- **Authorization** → Big improvement, with caveats (below). Per-service visibility enforced server-side; three roles; API keys hashed (HMAC-SHA256 + per-key salt + server pepper), zero-downtime rotation, individual revocation. Far cleaner than SharePoint ACLs.
- **Secure storage** → Improvement. A real database, hashed secrets, full audit log, soft-delete by default, and genuine GDPR tooling (erase/anonymise, DSAR export, retention purge). No more files wandering onto laptops.
- **Catalogue** → Solved. Code-free schemas with type/unit/cadence/required per value, plus version history.
- **Reporting fragility** → Solved. A stable OData v4 feed decouples Power BI from storage internals. Rename things internally and the feed contract holds.
- **Missing-data blindness** → Solved. A missing-submissions dashboard plus email reminders and outbound webhooks (`window.upcoming` / `window.missed`).
- **Manual effort** → Solved *if* services automate. Every console action is a documented REST endpoint, so a cron job or Azure Function pushes KPIs straight from source systems.

**Yes — it directly addresses every pain point listed.** That's rare and notable.

---

## 3. Is it a net gain?

Yes — but be clear-eyed about the trade. Today Microsoft effectively runs the storage layer for me (SharePoint just *exists*). With Ingest I trade **convenience for control and data quality.** I gain validated, governed, auditable, automatable data with a stable reporting contract. In exchange, **I now own the operations**: TLS, backups, HA, rate-limiting, and data residency are all *my* responsibility (the app deliberately delegates these to the hosting platform). For a small council IT team that's a real, ongoing cost — not a deal-breaker, but it must be staffed.

---

## 4. Does it have everything I need? What's missing?

**Genuinely strong:** core ingest, validation, cadence, catalogue, OData, API-key auth, audit, soft-delete, GDPR tooling, webhooks, email. The code largely backs the marketing.

**Missing or thin — the honest gaps:**

- **No approval / maker-checker workflow.** In a council, "official" KPIs often need a sign-off before they count. Today a service submits and it's instantly live; corrections are admin-only after the fact. For governance, the single biggest gap.
- **Coarse RBAC.** Only Service / Operator / Admin. An **Operator can read *every* service's data** — no department-scoped operator, no department admin, no delegation. A multi-department council will feel this.
- **Shallow SSO for a Microsoft shop.** Generic OIDC with *manual email pre-linking* per user — **no Entra group→role mapping, no auto-provisioning, no SCIM**, and off by default. If we're all-in on Entra, onboarding/offboarding is more manual than I'd want. Service accounts are API-key-only.
- **Long-lived API keys as the automation path.** Distribution and safe storage of those keys sits with each service; no mTLS or short-lived tokens yet (listed as "future").
- **Single tenant, single container, single DB = a SPOF** unless I deliberately turn on Cosmos HA and ≥1 replicas. The in-app backup tool is explicitly *not* production-grade (in-memory, non-transactional).
- **No compliance certifications** (no SOC 2 / ISO 27001 / Cyber Essentials). Self-hosted OSS, so the assurance burden is mine.
- **Accessibility & language.** English-only, and **no WCAG conformance claim**. UK public-sector services are legally required to meet WCAG 2.1 AA (PSBAR) — I'd need an audit before exposing the console broadly.
- **Validation can't look across time.** Rules are pure functions of the current submission + clock — no "must be ≥ last month" or external reference data. Some quality checks I'd want simply can't be expressed.
- **Maturity risk.** It's **v0.3.0** (pre-1.0, API still evolving). CI only builds a Docker image — **no automated test gate, no integration/HTTP tests, no frontend tests**, and the richest subsystem (the validator) lacks direct tests. Solid bones, but not battle-hardened.

---

## 5. What I'd change (if I could)

In priority order:
1. Add a **maker-checker approval workflow**.
2. **Entra group→role mapping + auto-provisioning**.
3. **Department-scoped RBAC / delegation**.
4. Harden **CI + integration/validator tests** before trusting it in production.
5. A **WCAG 2.1 AA audit**.
6. A documented **HA + backup/restore runbook** for ops.

---

## 6. The other two chairs

**Analyst / data-admin's view — the biggest winner.** Clean, typed, validated data through a stable OData feed they already know; no more reverse-engineering broken spreadsheets each cycle; a real catalogue; built-in missing-data visibility. Caveat: the built-in "Explore" is explicitly *not* a BI tool, so Power BI stays the analytics tier — which is fine, that's the point. The read-everything Operator role is actually convenient for them.

**Client service (the submitter)'s view — most mixed.** They lose the comfort of free-form Excel. The real payoff comes only when they *automate via the API* and stop typing numbers altogether — for teams that can't, the web form is a step sideways, not forward. Friction points: handling an API key responsibly, learning a new console, no offline editing, and **strict cadence means they can no longer quietly back-fill or double-submit** (great for governance, irritating for them). Needs genuine change management and training, or it'll be resented.

---

## 7. A message to myself

> **Re: Should we replace the Excel + SharePoint KPI setup with Ingest?**
>
> Yes — pilot it, with eyes open.
>
> **Why adopt:** It fixes the exact things that bleed us every reporting cycle — unvalidated data, no submission control, the SharePoint permissions swamp, and brittle Power BI refreshes. Validation and cadence enforcement alone stop the garbage at the door, which is worth more than it sounds: our dashboards become trustworthy, and the analysts stop laundering spreadsheets. The OData feed means Power BI never breaks again from someone renaming a column. The REST API is the real prize: the services that can automate will *stop hand-typing numbers*, killing a whole class of transcription errors and missed windows. And the code is real — not a README with nothing behind it.
>
> **Why be careful:** We stop renting Microsoft's storage and start *running a service*. TLS, HA, backups, rate-limiting and data residency become our job — so this only flies if Ops actually owns it. It's pre-1.0 with a thin CI and no integration tests, so I want a security review and our own smoke tests before production. RBAC is coarse (any Operator sees every service), there's **no approval workflow**, and SSO doesn't deeply integrate with Entra yet — all of which I can live with for a pilot but would want on the roadmap before council-wide rollout. And I must commission a WCAG audit before I put the console in front of staff at large.
>
> **Decision:** Run a 90-day pilot with 2–3 willing services (ideally ones that can hit the API), deployed on Container Apps + Cosmos for MongoDB vCore with HA on and a tested backup runbook. Keep SharePoint as the fallback during the pilot. Gate the wider rollout on: a security review, an approval-workflow decision (build, sponsor, or consciously accept its absence), the accessibility audit, and confidence that Ops can carry it. If those clear, migrate. The net gain in data quality and governance is real and hard to get any other way at this footprint.
>
> Don't let "but SharePoint is already there" win by default. *Already there* is exactly why our data can't be trusted.
