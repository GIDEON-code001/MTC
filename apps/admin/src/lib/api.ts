import type {
  DashboardSummary,
  FleetReport,
  Job,
  MaintenanceRecord,
  Motorcycle,
  PagedResponse,
  Payment,
  Rider,
  Session,
} from "@/lib/types";

const API_BASE_URL =
  process.env.NEXT_PUBLIC_API_BASE_URL?.replace(/\/+$/, "") ||
  "http://localhost:8080";

export class ApiError extends Error {
  constructor(
    message: string,
    public readonly status: number,
  ) {
    super(message);
    this.name = "ApiError";
  }
}

async function request<T>(
  path: string,
  token?: string,
  init?: RequestInit,
): Promise<T> {
  let response: Response;
  try {
    response = await fetch(`${API_BASE_URL}${path}`, {
      ...init,
      headers: {
        Accept: "application/json",
        ...(init?.body ? { "Content-Type": "application/json" } : {}),
        ...(token ? { Authorization: "Bearer " + token } : {}),
        ...init?.headers,
      },
      cache: "no-store",
    });
  } catch {
    throw new ApiError(
      `Could not connect to the BodaBoss API at ${API_BASE_URL}. Check the API URL and that the service is running.`,
      0,
    );
  }

  if (!response.ok) {
    let message = `The request failed (${response.status}).`;
    try {
      const body = (await response.json()) as {
        message?: string;
        title?: string;
        errors?: Record<string, string[]>;
      };
      message =
        body.message ??
        body.title ??
        Object.values(body.errors ?? {})[0]?.[0] ??
        message;
    } catch {
      // Some API errors, including 401 and 404, have no response body.
    }
    if (response.status === 401) message = "Your session has expired. Sign in again.";
    throw new ApiError(message, response.status);
  }

  if (response.status === 204) return undefined as T;
  return (await response.json()) as T;
}

export const api = {
  login(input: { tenantId: string; identifier: string; password: string }) {
    return request<Session>("/api/v1/auth/login", undefined, {
      method: "POST",
      body: JSON.stringify(input),
    });
  },
  dashboard(token: string) {
    return request<DashboardSummary>("/api/v1/dashboard/summary", token);
  },
  riders(token: string) {
    return request<PagedResponse<Rider>>("/api/v1/riders?page=1&pageSize=50", token);
  },
  motorcycles(token: string) {
    return request<PagedResponse<Motorcycle>>(
      "/api/v1/motorcycles?page=1&pageSize=50",
      token,
    );
  },
  jobs(token: string) {
    return request<PagedResponse<Job>>("/api/v1/jobs?page=1&pageSize=50", token);
  },
  payments(token: string) {
    return request<PagedResponse<Payment>>(
      "/api/v1/payments?page=1&pageSize=50",
      token,
    );
  },
  reports(token: string) {
    return request<PagedResponse<FleetReport>>(
      "/api/v1/reports?page=1&pageSize=50",
      token,
    );
  },
  maintenance(token: string) {
    return request<PagedResponse<MaintenanceRecord>>(
      "/api/v1/maintenance?page=1&pageSize=50",
      token,
    );
  },
};

export const defaultTenantId =
  process.env.NEXT_PUBLIC_TENANT_ID ?? "11111111-1111-4111-8111-111111111111";
