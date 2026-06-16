# Security policy

Ingest is an open-source project maintained by a single developer on a best-effort basis. Security reports are taken seriously, but please read the expectations below so you know what you're getting.

## Reporting a vulnerability

**Please report security issues privately. Do not open a public GitHub issue, pull request, or discussion for a vulnerability.**

The preferred channel is **GitHub's private vulnerability reporting**:

1. Go to the repository's **Security** tab → **Report a vulnerability** (GitHub Security Advisories).
2. Describe the issue with enough detail to reproduce it: affected version / image tag, deployment configuration, impact, and a proof-of-concept or steps if you have one.

This keeps the report private to the maintainer until a fix is available.

If private advisories are unavailable to you for some reason, contact the maintainer through the address listed on their GitHub profile rather than posting publicly.

## What to expect

- **Best-effort handling.** There is no security team and **no guaranteed response or remediation time**. Reports are reviewed and acted on as time allows.
- **No bug bounty.** There is no monetary reward program. Credit in the advisory / release notes is offered with thanks (opt out if you'd prefer to stay anonymous).
- **Coordinated disclosure.** Please give a reasonable window to investigate and ship a fix before disclosing publicly. A fixed issue will be published as a GitHub Security Advisory.
- **As-is.** Nothing here changes the [MIT licence](LICENSE) terms — the software is provided without warranty.

## Supported versions

This is a small project without a formal release-support matrix. In practice, **only the latest version on the default branch** receives fixes. There are no backports to older tags. If you run a pinned image, plan to update to pick up a security fix.

## Scope and your responsibilities

Ingest is a self-hosted application: **how you deploy and operate it is the largest part of your security posture**, and that part is yours, not the maintainer's. In particular:

- **Secrets.** Set a strong `ApiKey:Pepper` and protect it; protect your Mongo connection string and SMTP/webhook credentials. See [configuration](docs/setup/configuration.md).
- **Transport & network.** Terminate TLS at your ingress and apply rate limiting / IP restrictions there — the app does not do this for you (see [hosting § network controls](docs/setup/hosting.md)).
- **Auth model.** Understand the API-key and optional SSO model before going live: [architecture/authentication.md](docs/architecture/authentication.md).
- **Data protection.** Read [gdpr.md](docs/gdpr.md) — the product ships erasure/export/retention tooling, but the controller responsibilities remain yours.
- **Backups & recovery.** Follow [disaster-recovery](docs/setup/disaster-recovery.md); the in-app backup tool is a convenience, not your primary safety net.

Reports about issues that are purely a consequence of a misconfigured deployment (rather than a flaw in the code) are still welcome as documentation feedback, but may be closed as "working as designed."
