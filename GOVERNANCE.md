# Governance

This is a short, honest description of how decisions get made on Ingest and what that means for anyone depending on it. It exists mainly to answer one question evaluators reasonably ask: *"what happens if the maintainer disappears?"*

## Who decides

Ingest currently has **one maintainer**, who has final say on scope, design, what gets merged, and releases. There is no committee, no voting, and no formal roadmap process — the project keeps process light by design (see [CONTRIBUTING.md](CONTRIBUTING.md)).

This means decisions are fast but also a **bus factor of one**: if the maintainer steps away, active development pauses.

## What that means for you

That risk is real, and the project is structured so it never traps you:

- **MIT licence.** You can fork and maintain your own copy at any time, for any reason, forever. You are not locked in to this repository.
- **Small, documented, conventional.** The codebase is deliberately small and layered (Core / Infrastructure / Api — see [architecture](docs/architecture/README.md)), every public type carries XML docs, and operations are covered end-to-end in [docs/](docs/README.md). A competent .NET team can take it over without tribal knowledge.
- **Standard stack.** ASP.NET Core, MongoDB, React + Fluent UI, Docker — nothing exotic to inherit.

## Becoming a co-maintainer

**Co-maintainers are genuinely welcome** — sharing the load is the best way to raise the bus factor, and the project would rather grow a small maintainer team than stay a solo effort.

There's no bureaucratic process. The path is simply:

1. **Contribute a few changes** — bug fixes, docs, or features — via pull requests (see [CONTRIBUTING.md](CONTRIBUTING.md)). This builds shared context and trust.
2. **Open a discussion** at https://github.com/arepetti/ingest/discussions saying you'd like to help maintain the project, and roughly which areas interest you (API, SPA, docs, ops).
3. The maintainer will talk it through with you and, when it's a good fit, grant commit access and add you to a `MAINTAINERS` list.

Maintainers are expected to keep the [contribution bar](CONTRIBUTING.md) (build green, tests passing, docs updated alongside code) and to handle changes the same way they'd want their own reviewed.

## Decision-making for changes

- **Small, focused changes** that fit the project's scope and keep the docs in sync are the easiest to accept.
- **Larger or scope-expanding changes** are best raised as an issue or discussion *first*, so effort isn't spent on something that won't be merged. Ingest intentionally favours a small, well-defined scope over feature breadth.
- **Declined contributions** aren't a judgement on the work — often it's just scope. You can always carry a change in a fork.

## Support and security

Governance is about *who decides*; for *how to get help* and *how to report vulnerabilities*, see [SUPPORT.md](SUPPORT.md) and [SECURITY.md](SECURITY.md).
