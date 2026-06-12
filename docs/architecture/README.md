# Architecture

The maintainer/contributor section of the docs: how the system is put together and how it authenticates its callers.

| Page                                       | What's inside                                                                                                                |
|--------------------------------------------|------------------------------------------------------------------------------------------------------------------------------|
| [architecture.md](architecture.md)         | Solution layout, domain model, request flow, validation pipeline, cadence semantics, the email & notifications subsystem, Aspire orchestration, configuration, Mongo indexes, testing strategy. |
| [authentication.md](authentication.md)     | How API keys are produced, stored, verified, rotated, revoked. Threat model, roles, kinds, bootstrap admin, configuration knobs. |

The source itself (with the XML documentation comments generated into Swagger) is the next step after these two pages. The companion practical guides live in:

- [../admin-user-guide/README.md](../admin-user-guide/README.md) — operating the system day-to-day.
- [../client/README.md](../client/README.md) — calling the API as a service.
- [../setup/README.md](../setup/README.md) — deploying and connecting reporting tools.
