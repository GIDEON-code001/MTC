# BodaBoss Admin Portal

Tenant-aware Next.js 15 admin UI for the BodaBoss API.

## Run locally

1. Copy `.env.example` to `.env.local` and set `NEXT_PUBLIC_API_BASE_URL` and `NEXT_PUBLIC_TENANT_ID` for your environment.
2. From this directory run `npm install`, `npm run dev`, then open `http://localhost:5000`.

The development and production start scripts use port 5000 to match the API's default development CORS origin. If you use another origin, configure that origin in the API's allowed CORS origins. Build with `npm run build`; run a production build with `npm start`.

## API coverage

The portal signs in with `/api/v1/auth/login` using a workspace ID and email or Ugandan phone number. It uses the returned tenant-scoped bearer token for dashboard summary, riders, motorcycles, jobs, payments, reports, and maintenance. Navigation and section access are filtered by the authenticated role.

Reports, maintenance, analytics, notifications, and a live GPS feed are not implemented by the current API. Those sections state this limitation instead of displaying generated data. The API only reports a recent tracked count, motorcycle coordinates on the motorcycle list, and accepts a manual GPS fix; this app does not simulate a map, stream, or location history.
