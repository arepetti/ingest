# Quickstart — try Ingest in 5 minutes

This is the fastest way to see Ingest running on your own machine. **You do not need the .NET SDK, Node.js, or MongoDB** — the only prerequisite is **Docker** ([Docker Desktop](https://www.docker.com/products/docker-desktop/) on Windows/macOS, or Docker Engine on Linux).

If you're a developer who wants the hot-reload Aspire setup instead, see [CONTRIBUTING.md](../../CONTRIBUTING.md).

> **This is an evaluation setup.** It ships with well-known placeholder secrets and is not safe to expose to an untrusted network. When you're ready for real, follow [hosting.md](hosting.md).

## Windows: the one-click way

On Windows you don't have to touch the command line at all. Double-click:

```
scripts\try-ingest.cmd
```

It checks whether Docker is installed, offers to install Docker Desktop for you (via `winget`, asking first), starts the Docker engine if needed, and then launches everything. If a step needs you to do something by hand (like the one-time Docker Desktop sign-in), it tells you exactly what. When it's done it prints the URL and the sign-in key below.

Prefer to drive it yourself, or on macOS/Linux? Use one of the options below.

## Option A — Docker Compose (recommended)

This builds the image from source inside Docker (so you still don't need any SDK) and starts MongoDB alongside it.

From the repository root:

```bash
docker compose up --build
```

The first run takes a few minutes while Docker builds the SPA and the API. When you see the app reporting that it's listening on port `8080`, open:

- **Admin console:** <http://localhost:8080>
- **Swagger / API explorer:** <http://localhost:8080/swagger>

Sign in on the console with this API key (it's preconfigured in `docker-compose.yml`):

```
localdev.local-dev-admin-key-change-me
```

That's the admin key. The same value goes in the `X-Api-Key` header for direct API calls, e.g.:

```bash
curl http://localhost:8080/api/me -H "X-Api-Key: localdev.local-dev-admin-key-change-me"
```

### Stopping and cleaning up

```bash
docker compose down       # stop the containers, keep the data
docker compose down -v     # stop and delete the MongoDB data volume too
```

## Option B — Use the published image

Once someone has run the [Build and publish Docker image](../../.github/workflows/docker-image.yml) GitHub Action for this repository, a ready-built image is available on the GitHub Container Registry and you can skip the build step entirely.

The image is published as `ghcr.io/<owner>/<repo>` (all lowercase) — for example `ghcr.io/your-org/ingest:latest`. If the package is private, run `docker login ghcr.io` first (use a GitHub token with `read:packages`).

You still need a MongoDB to point it at. The simplest is two containers on a shared network:

```bash
# 1. A throwaway MongoDB
docker network create ingest-net
docker run -d --name ingest-mongo --network ingest-net mongo:7

# 2. The app (replace <owner>/<repo> with the real image path)
docker run --rm --name ingest --network ingest-net -p 8080:8080 \
  -e ConnectionStrings__ingest="mongodb://ingest-mongo:27017/ingest" \
  -e ApiKey__Pepper="local-dev-pepper-change-me" \
  -e ApiKey__BootstrapAdminKey="localdev.local-dev-admin-key-change-me" \
  -e Ingest__EnableSwagger="true" \
  ghcr.io/<owner>/<repo>:latest
```

Then sign in exactly as in Option A.

To tear it down:

```bash
docker rm -f ingest ingest-mongo
docker network rm ingest-net
```

## What just happened

- The container bundles the REST API **and** the admin SPA, served from the same origin — so `http://localhost:8080` is both the website and the API.
- On first boot the app created an **admin account** and gave it the key you configured via `ApiKey__BootstrapAdminKey`. Because you set the key yourself, you never had to dig it out of the logs. (Leave that setting empty in production and the app falls back to generating a random key and printing it once — see [authentication.md](../architecture/authentication.md#the-bootstrap-admin).)
- MongoDB stores everything. In Option A it lives in the `ingest-mongo-data` Docker volume and survives restarts until you `docker compose down -v`.

## Next steps

- **Kick the tyres as an admin.** Create a schema, issue a service key, post a submission. The [admin user guide](../admin-user-guide/README.md) walks through each task.
- **Call the API.** The [client guide](../client/README.md) and [api.md](../client/api.md) show how a service authenticates and submits data programmatically.
- **Change the secrets.** Before doing anything beyond a local trial, set your own `ApiKey__Pepper` and `ApiKey__BootstrapAdminKey` (or remove the latter and read the generated key from the logs). See [configuration.md](configuration.md).
- **Deploy it for real.** [hosting.md](hosting.md) is the end-to-end Azure walkthrough — or run it on Azure for ~$0 with its [free-tier walkthrough](hosting.md#free-tier---a-0-evaluation-deployment).

## Troubleshooting

| Symptom | Likely cause / fix |
|---------|--------------------|
| `docker compose up` fails immediately with a port error | Something is already on port `8080`. Edit the `ports:` mapping in `docker-compose.yml` to e.g. `"9090:8080"` and use that port instead. |
| Login says the key is invalid | Make sure you typed the whole key including the dot: `localdev.local-dev-admin-key-change-me`. If you changed `ApiKey__BootstrapAdminKey` *after* the first boot, the original key is still the active one — `docker compose down -v` to start fresh. |
| The app container keeps restarting | It usually can't reach MongoDB. In Option A the app waits for Mongo's healthcheck; check `docker compose logs mongo`. In Option B make sure both containers are on the same `--network`. |
| Blank screen after signing in | The key belonged to a disabled/deleted account. Clear the site's `localStorage` and sign in again with the bootstrap key. |
