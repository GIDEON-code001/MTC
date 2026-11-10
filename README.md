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

## Oracle Cloud Always Free deployment

The OCI Compose stack runs PostgreSQL, the ASP.NET Core API, the Next.js admin portal, and Caddy on one VM. PostgreSQL and the application containers are not published directly; Caddy routes HTTPS traffic to the admin portal and `/api/*` plus `/health/*` requests to the API. This single-VM setup is for a preview or small pilot, not a high-availability deployment or a validated 10,000-motorcycle production environment. OCI Always Free resources have limits and may be reclaimed when idle; keep independent database backups.

1. Create an Oracle Cloud Free Tier account and provision only an **Always Free eligible** Ubuntu Ampere A1 VM (up to 2 OCPUs and 12 GB memory under the current free limits). Do not upgrade the account or choose paid resources. Free capacity may not be available in every region.
2. Assign a public IP and configure OCI network ingress for SSH (restrict port 22 to your own IP when possible) and TCP ports 80 and 443. Point a domain you control to the VM's public IP with a DNS A record; remove an incorrect AAAA record if the VM has no IPv6.
3. SSH into the VM using the private key downloaded during instance creation. Keep that key on your computer; never send it through chat. Install Git, OpenSSL, Docker Engine, and the Docker Compose v2 plugin, then clone this public repository:

   ```sh
   git clone https://github.com/GIDEON-code001/MTC.git
   cd MTC
   ```

4. After the domain resolves to the VM and ports 80/443 are reachable, create the private environment file and launch the services:

   ```sh
   bash deployment/configure-oci.sh
   bash deployment/deploy-oci.sh
   ```

   The configure script prompts for the public domain, certificate email, and bootstrap admin credentials. It generates database/JWT secrets on the VM and writes them to `.env.oci` with owner-only permissions. It will not overwrite an existing secret file. Do not copy `.env.oci` into Git, logs, or chat.
5. Verify the deployment at `https://<your-domain>/health/ready` and open `https://<your-domain>`. Use the administrator credentials entered during configuration. For the rider app, set its `API_BASE_URL` to `https://<your-domain>`.

The domain must resolve publicly before Caddy can issue a trusted HTTPS certificate. The VM, OS updates, firewall rules, backups, and monitoring remain your responsibility. Do not deploy real fleet or payment data until backups and recovery have been tested. Oracle's Free Tier and Always Free terms and capacity limits apply; this stack does not guarantee permanent free capacity or production availability.

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
