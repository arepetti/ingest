# Support

Thanks for using **Ingest**. This page explains what kind of support exists, where to ask, and what you can realistically expect — so you can make an informed call before relying on it.

## The short version

Ingest is an open-source project maintained by **a single developer in their spare time**. It is offered under the [MIT licence](LICENSE) **as-is, with no warranty and no service-level agreement (SLA)**. There is no company behind it, no support contract, and no guaranteed response time.

That isn't a disclaimer to scare you off — it's so you can plan. Plenty of teams run software like this happily; the trick is to **plan to self-support** rather than expecting a vendor on the other end of a phone.

## Where to get help

| You want to… | Go here |
|--------------|---------|
| Ask a question, get usage help, share what you built | **GitHub Discussions** — https://github.com/arepetti/ingest/discussions |
| Report a reproducible bug or request a feature | **GitHub Issues** — https://github.com/arepetti/ingest/issues |
| Report a security vulnerability | **Privately** — see [SECURITY.md](SECURITY.md). Do **not** open a public issue. |
| Understand how something works | The [documentation](docs/README.md) first — it's extensive and audience-split. |

Before opening an issue, a quick search of existing issues and the [docs](docs/README.md) (especially [troubleshooting](docs/admin-user-guide/troubleshooting.md)) often gets you there faster.

### A good bug report

The faster a problem can be understood, the faster it gets looked at:

- What you expected to happen, and what actually happened.
- Steps to reproduce (a minimal schema / submission payload is gold).
- Version / image tag, deployment target (Container Apps, self-hosted, quickstart, …), and relevant log lines.
- For API issues: the request, the response status, and the `errors[]`/`title` from the problem-details body.

## What you can expect

- **Best-effort only.** Issues and discussions are read and responded to when time allows. **There is no committed response time** — it might be a day, it might be much longer, and some requests will be declined or left for a contributor to pick up.
- **No SLA, no warranty.** Nothing here changes the [MIT licence](LICENSE) terms. Don't deploy Ingest into a critical path expecting guaranteed fixes on a timeline.
- **Fixes are not guaranteed.** A well-scoped pull request is far more likely to land than a request for someone else to do the work.

## If you need more certainty

If your use case needs guarantees this project can't offer, you have real options — and the project is designed to make them painless:

- **Self-support.** The code is small and deliberately layered (see [architecture](docs/architecture/README.md)), every public type carries XML docs, and the docs cover deployment, configuration, and disaster recovery. A competent .NET team can own and extend it.
- **Fork it.** The MIT licence lets you fork and maintain your own copy indefinitely — you are never locked in to this repository or its maintainer.
- **Help maintain it.** If you'd like to take on more than a one-off fix, see [GOVERNANCE.md](GOVERNANCE.md) — co-maintainers are welcome.

## Contributing

Patches, docs improvements, and well-described issues are all genuinely appreciated. See [CONTRIBUTING.md](CONTRIBUTING.md) for the dev environment, tests, and how to submit changes.
