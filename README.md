# BodaBoss

BodaBoss is a fleet operations platform for boda boda businesses in Uganda. It contains a Next.js fleet portal, a Flutter rider app, a tenant-aware ASP.NET Core API, and PostgreSQL.

## Repository layout

- `apps/admin` — Next.js fleet operations portal
- `apps/mobile` — Flutter rider app with Home, Jobs, Report, Payments, and Notifications tabs
- `src/BodaBoss.Api` — ASP.NET Core REST API
- `tests/BodaBoss.Api.Tests` — API domain unit tests
- `database/init.sql` — initial PostgreSQL schema, applied by Docker on first database creation
- `docker-compose.yml` — local PostgreSQL and API services

## Requirements

- .NET 8 SDK
- Flutter stable SDK
- Docker Desktop with Compose

## Start the backend

1. Copy `.env.example` to `.env`. Set a unique database password and a random JWT signing key of at least 32 bytes. Set the bootstrap email and a unique password of at least 12 characters together. Use a hexadecimal database password (for example, output from `openssl rand -hex 32`) so it is safe in the PostgreSQL connection string.
2. Start the services:

   ```powershell
   docker compose up --build
   ```

The API is available at `http://localhost:8080`; health checks are at `/health/live` and `/health/ready`. PostgreSQL is exposed on port `5432` for local development. The schema script runs only when PostgreSQL initializes a new data directory.

To run the API without Docker, start PostgreSQL, apply `database/init.sql`, set the configuration variables from `.env.example` in your shell, and run:

```powershell
dotnet run --project src/BodaBoss.Api
```

## Start the admin portal

```powershell
cd apps/admin
npm install
npm run dev
```

Configure `NEXT_PUBLIC_API_BASE_URL` and `NEXT_PUBLIC_TENANT_ID` in `apps/admin/.env.local` as needed. The development server runs on port `5000`.

## Render preview deployment

Configure the admin Node service with root directory `apps/admin`, build command `npm ci && npm run build`, and start command `npm run start`. Deploy the API as a Docker web service with repository root as its Docker context and `src/BodaBoss.Api/Dockerfile` as its Dockerfile. Set the API's `DATABASE_URL` from Render Postgres and `Database__InitializeSchema=true` to initialize an empty preview database. Set `Jwt__SigningKey` to a generated secret and `Cors__AllowedOrigins__0` to the admin service's HTTPS URL. Before enabling sign-in, provision `Bootstrap__Email` and `Bootstrap__Password` together in Render's secret environment editor; use a password of at least 12 characters. Set `NEXT_PUBLIC_API_BASE_URL` to the API's HTTPS URL before building the admin service. Render free services can sleep, and free databases may be temporary; use backed-up, paid infrastructure for production.

## API outline

- `POST /api/v1/auth/login` and `POST /api/v1/auth/refresh` — tenant-scoped login and rotating refresh tokens
- `GET /api/v1/dashboard/summary` — operational KPIs
- `GET|POST /api/v1/riders`, `GET|POST /api/v1/motorcycles` — fleet registry
- `GET|POST /api/v1/jobs`, `PUT /api/v1/jobs/{id}/assign` — create and assign rider jobs
- `/api/v1/rider/*` — rider home, job actions, issue reports, payment history, GPS fixes, devices, and notifications
- `/api/v1/reports` and `/api/v1/maintenance` — fleet issue triage and service schedules
- `GET /api/v1/motorcycles/{id}/locations` — GPS history
- `GET|POST /api/v1/payments` — administrative payment records in integer UGX

Business endpoints require a bearer token and are tenant-scoped. Paginated lists accept `page` and `pageSize` (maximum 100). Rider accounts are provisioned by an authorized fleet manager; `/api/v1/riders` requires a password of at least 12 characters. The bootstrap administrator is created when both `Bootstrap__Email` and `Bootstrap__Password` are configured. Keep those values configured on restarts; they do not reset an existing password. The tenant ID defaults to `11111111-1111-4111-8111-111111111111`.

Run API unit tests with:

```powershell
dotnet test tests/BodaBoss.Api.Tests/BodaBoss.Api.Tests.csproj
```

## Start the rider app

```powershell
cd apps/mobile
flutter pub get
flutter run -d chrome --dart-define=API_BASE_URL=http://localhost:8080
```

For Android emulators, use `http://10.0.2.2:8080` as `API_BASE_URL`. Configure Firebase with FlutterFire for each target before enabling push notifications. Use an HTTPS API URL outside local development.

## Production rollout notes

This repository is a deployable foundation, not a claim that every operational and regulatory workflow is complete. Mobile-money provider collection/remittance, Cloudflare R2 media upload, Traccar ingestion, and Firebase push delivery still require provider credentials and production integration; recording an administrative payment is not a provider-confirmed transfer. Before production, configure managed PostgreSQL with backups and TLS, provision secrets through a secret manager, apply reviewed schema migrations, configure HTTPS and allowed origins, and verify payment callbacks before marking provider transactions successful. Configure trusted reverse-proxy IPs with `ForwardedHeaders__KnownProxies__0` (and additional indexed entries) so rate limiting uses the real client IP. Use a queue and retention policies for high-volume telemetry and payment webhooks.

The schema includes tenant identifiers and indexes for fleet operations. For fleets approaching 10,000+ motorcycles, deploy multiple stateless API instances behind a load balancer, partition/archive high-volume trip and location history based on measured query patterns, and monitor database connection pools and API latency.
