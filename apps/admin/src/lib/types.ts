export type UserRole =
  | "Super Admin"
  | "Operations Manager"
  | "Finance Officer"
  | "Admin"
  | string;

export interface Session {
  accessToken: string;
  refreshToken: string;
  expiresAt: string;
  email: string | null;
  role: UserRole;
}

export interface DashboardSummary {
  fleetCount: number;
  availableCount: number;
  activeTrips: number;
  trackedCount: number;
  revenueTodayUgx: number;
  recentPayments: Array<{
    id: string;
    amountUgx: number;
    method: string;
    status: string;
    receivedAt: string;
  }>;
}

export interface PagedResponse<T> {
  items: T[];
  page: number;
  pageSize: number;
  totalCount: number;
}

export interface Motorcycle {
  id: string;
  registrationNumber: string;
  make: string;
  model: string;
  year: number;
  status: string;
  latitude: number | null;
  longitude: number | null;
  locationRecordedAt: string | null;
}

export interface Rider {
  id: string;
  fullName: string;
  phoneNumber: string;
  licenseNumber: string;
  status: string;
}

export interface Job {
  id: string;
  pickupLocation: string;
  dropoffLocation: string;
  instructions: string | null;
  amountUgx: number;
  deadline: string;
  status: string;
  acceptedAt: string | null;
  startedAt: string | null;
  completedAt: string | null;
}

export interface Payment {
  id: string;
  driverId: string;
  motorcycleId: string | null;
  amountUgx: number;
  method: string;
  status: string;
  receivedAt: string;
}

export interface FleetReport {
  id: string;
  riderId: string;
  motorcycleId: string | null;
  issueType: string;
  description: string;
  latitude: number | null;
  longitude: number | null;
  status: string;
  resolution: string | null;
  createdAt: string;
  resolvedAt: string | null;
}

export interface MaintenanceRecord {
  id: string;
  motorcycleId: string;
  serviceType: string;
  dueAtMileageKm: number;
  completedAtMileageKm: number | null;
  costUgx: number;
  status: string;
  dueAt: string | null;
  completedAt: string | null;
  notes: string | null;
}
