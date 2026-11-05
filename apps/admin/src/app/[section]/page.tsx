"use client";

import { useQuery } from "@tanstack/react-query";
import { Bike, CircleDollarSign, MapPin, Route, UsersRound, Wrench } from "lucide-react";
import { useParams } from "next/navigation";
import { useAuth, canAccessSection } from "@/lib/auth";
import { api } from "@/lib/api";
import { PageGuard, PageHeading } from "@/components/app-shell";
import { EmptyState, ErrorState, LoadingState, UnavailableState } from "@/components/data-state";
import { StatusTag } from "@/components/status-tag";

const currencyFormat = new Intl.NumberFormat("en-UG", {
  style: "currency",
  currency: "UGX",
  maximumFractionDigits: 0,
});

const pages: Record<string, { title: string; description: string }> = {
  riders: { title: "Riders", description: "Manage and review the rider records in your fleet." },
  motorcycles: { title: "Motorcycles", description: "View registered motorcycles and their reported status." },
  jobs: { title: "Jobs", description: "Review rider assignments and delivery progress." },
  payments: { title: "Payments", description: "Review payments recorded by your BodaBoss account." },
  reports: { title: "Reports", description: "Business reporting and downloadable summaries." },
  maintenance: { title: "Maintenance", description: "Maintenance records and service schedules." },
  gps: { title: "GPS tracking", description: "Location availability from the connected fleet API." },
  analytics: { title: "Analytics", description: "Operational trends and fleet performance." },
  notifications: { title: "Notifications", description: "Updates and alerts for your fleet operations." },
};

export default function SectionPage() {
  const params = useParams<{ section: string }>();
  const section = params.section;
  return (
    <PageGuard>
      <SectionContent section={section} />
    </PageGuard>
  );
}

function SectionContent({ section }: { section: string }) {
  const { session } = useAuth();
  const riders = useQuery({
    queryKey: ["riders", session?.accessToken],
    queryFn: () => api.riders(session!.accessToken),
    enabled: Boolean(session && section === "riders"),
    refetchInterval: 60_000,
  });
  const motorcycles = useQuery({
    queryKey: ["motorcycles", session?.accessToken],
    queryFn: () => api.motorcycles(session!.accessToken),
    enabled: Boolean(session && (section === "motorcycles" || section === "gps")),
    refetchInterval: 60_000,
  });
  const jobs = useQuery({
    queryKey: ["jobs", session?.accessToken],
    queryFn: () => api.jobs(session!.accessToken),
    enabled: Boolean(session && section === "jobs"),
    refetchInterval: 60_000,
  });
  const payments = useQuery({
    queryKey: ["payments", session?.accessToken],
    queryFn: () => api.payments(session!.accessToken),
    enabled: Boolean(session && section === "payments"),
    refetchInterval: 60_000,
  });
  const reports = useQuery({
    queryKey: ["reports", session?.accessToken],
    queryFn: () => api.reports(session!.accessToken),
    enabled: Boolean(session && section === "reports"),
    refetchInterval: 60_000,
  });
  const maintenance = useQuery({
    queryKey: ["maintenance", session?.accessToken],
    queryFn: () => api.maintenance(session!.accessToken),
    enabled: Boolean(session && section === "maintenance"),
    refetchInterval: 60_000,
  });
  const page = pages[section];

  if (!page) {
    return <div className="page-stack"><PageHeading title="Page not found" description="This section doesn’t exist in the admin portal." /></div>;
  }
  if (!canAccessSection(session?.role ?? "", section)) {
    return (
      <div className="page-stack">
        <PageHeading eyebrow="ACCESS" title={page.title} description={page.description} />
        <UnavailableState title="This section isn’t available for your role" description="Ask your workspace administrator if you need access to this section." />
      </div>
    );
  }

  if (section === "riders") {
    return <DataPage title={page.title} description={page.description} query={riders} emptyTitle="No riders found" emptyCopy="Rider records will appear here when they are registered." icon={<UsersRound size={20} />}>
      {riders.data && <div className="table-scroll"><table><thead><tr><th>Rider</th><th>Phone</th><th>License</th><th>Status</th></tr></thead><tbody>
        {riders.data.items.map((rider) => <tr key={rider.id}><td><span className="record-name"><span className="record-avatar">{rider.fullName.slice(0, 1).toUpperCase()}</span><span><strong>{rider.fullName}</strong><small>#{rider.id.slice(0, 8)}</small></span></span></td><td>{rider.phoneNumber}</td><td>{rider.licenseNumber}</td><td><StatusTag value={rider.status} /></td></tr>)}
      </tbody></table></div>}
    </DataPage>;
  }
  if (section === "motorcycles") {
    return <DataPage title={page.title} description={page.description} query={motorcycles} emptyTitle="No motorcycles found" emptyCopy="Motorcycle records will appear here when they are registered." icon={<Bike size={20} />}>
      {motorcycles.data && <div className="table-scroll"><table><thead><tr><th>Motorcycle</th><th>Make & model</th><th>Year</th><th>Status</th><th>Last location report</th></tr></thead><tbody>
        {motorcycles.data.items.map((bike) => <tr key={bike.id}><td><span className="record-name"><span className="record-avatar"><Bike size={15} /></span><span><strong>{bike.registrationNumber}</strong><small>#{bike.id.slice(0, 8)}</small></span></span></td><td>{bike.make} {bike.model}</td><td>{bike.year}</td><td><StatusTag value={bike.status} /></td><td>{bike.locationRecordedAt ? new Date(bike.locationRecordedAt).toLocaleString("en-UG", { dateStyle: "medium", timeStyle: "short" }) : <span className="muted-text">No location reported</span>}</td></tr>)}
      </tbody></table></div>}
    </DataPage>;
  }
  if (section === "jobs") {
    return <DataPage title={page.title} description={page.description} query={jobs} emptyTitle="No jobs found" emptyCopy="Jobs will appear here when they are created." icon={<Route size={20} />}>
      {jobs.data && <div className="table-scroll"><table><thead><tr><th>Pickup</th><th>Drop-off</th><th>Deadline</th><th>Amount</th><th>Status</th></tr></thead><tbody>
        {jobs.data.items.map((job) => <tr key={job.id}><td><span className="record-name"><span className="record-avatar"><Route size={15} /></span><span><strong>{job.pickupLocation}</strong><small>#{job.id.slice(0, 8)}</small></span></span></td><td>{job.dropoffLocation}</td><td>{new Date(job.deadline).toLocaleString("en-UG", { dateStyle: "medium", timeStyle: "short" })}</td><td className="amount-cell">{currencyFormat.format(job.amountUgx)}</td><td><StatusTag value={job.status} /></td></tr>)}
      </tbody></table></div>}
    </DataPage>;
  }
  if (section === "payments") {
    return <DataPage title={page.title} description={page.description} query={payments} emptyTitle="No payments found" emptyCopy="Payments returned by your BodaBoss account will appear here." icon={<CircleDollarSign size={20} />}>
      {payments.data && <div className="table-scroll"><table><thead><tr><th>Payment</th><th>Rider ID</th><th>Method</th><th>Received</th><th>Amount</th><th>Status</th></tr></thead><tbody>
        {payments.data.items.map((payment) => <tr key={payment.id}><td><span className="record-name"><span className="record-avatar payment-avatar"><CircleDollarSign size={15} /></span><span><strong>Payment</strong><small>#{payment.id.slice(0, 8)}</small></span></span></td><td className="mono-cell">#{payment.driverId.slice(0, 8)}</td><td>{payment.method}</td><td>{new Date(payment.receivedAt).toLocaleString("en-UG", { dateStyle: "medium", timeStyle: "short" })}</td><td className="amount-cell">{currencyFormat.format(payment.amountUgx)}</td><td><StatusTag value={payment.status} /></td></tr>)}
      </tbody></table></div>}
    </DataPage>;
  }
  if (section === "reports") {
    return <DataPage title={page.title} description={page.description} query={reports} emptyTitle="No reports found" emptyCopy="Rider-submitted issues will appear here." icon={<Route size={20} />}>
      {reports.data && <div className="table-scroll"><table><thead><tr><th>Issue</th><th>Rider</th><th>Reported</th><th>Location</th><th>Status</th></tr></thead><tbody>
        {reports.data.items.map((report) => <tr key={report.id}><td><span className="record-name"><span className="record-avatar"><Route size={15} /></span><span><strong>{report.issueType}</strong><small>{report.description}</small></span></span></td><td className="mono-cell">#{report.riderId.slice(0, 8)}</td><td>{new Date(report.createdAt).toLocaleString("en-UG", { dateStyle: "medium", timeStyle: "short" })}</td><td>{report.latitude == null || report.longitude == null ? "—" : `${report.latitude.toFixed(5)}, ${report.longitude.toFixed(5)}`}</td><td><StatusTag value={report.status} /></td></tr>)}
      </tbody></table></div>}
    </DataPage>;
  }
  if (section === "maintenance") {
    return <DataPage title={page.title} description={page.description} query={maintenance} emptyTitle="No maintenance records" emptyCopy="Scheduled service records will appear here." icon={<Wrench size={20} />}>
      {maintenance.data && <div className="table-scroll"><table><thead><tr><th>Motorcycle</th><th>Service</th><th>Due</th><th>Cost</th><th>Status</th></tr></thead><tbody>
        {maintenance.data.items.map((item) => <tr key={item.id}><td className="mono-cell">#{item.motorcycleId.slice(0, 8)}</td><td>{item.serviceType}</td><td>{item.dueAt ? new Date(item.dueAt).toLocaleDateString("en-UG") : `${item.dueAtMileageKm.toLocaleString("en-UG")} km`}</td><td className="amount-cell">{currencyFormat.format(item.costUgx)}</td><td><StatusTag value={item.status} /></td></tr>)}
      </tbody></table></div>}
    </DataPage>;
  }
  if (section === "gps") {
    const tracked = motorcycles.data?.items.filter((bike) => bike.latitude != null && bike.longitude != null) ?? [];
    return <DataPage title={page.title} description={page.description} query={motorcycles} emptyTitle="No GPS fixes yet" emptyCopy="Motorcycle coordinates will appear here after a rider reports their location." icon={<MapPin size={20} />}>
      {motorcycles.data && motorcycles.data.items.length > 0 && tracked.length === 0 && <EmptyState title="No GPS fixes yet" description="Motorcycle coordinates will appear here after a rider reports their location." icon={<MapPin size={20} />} />}
      {tracked.length > 0 && <div className="table-scroll"><table><thead><tr><th>Motorcycle</th><th>Coordinates</th><th>Last report</th><th>Status</th></tr></thead><tbody>
        {tracked.map((bike) => <tr key={bike.id}><td><strong>{bike.registrationNumber}</strong></td><td>{bike.latitude?.toFixed(5)}, {bike.longitude?.toFixed(5)}</td><td>{bike.locationRecordedAt ? new Date(bike.locationRecordedAt).toLocaleString("en-UG", { dateStyle: "medium", timeStyle: "short" }) : "—"}</td><td><StatusTag value={bike.status} /></td></tr>)}
      </tbody></table></div>}
    </DataPage>;
  }

  const unavailable: Record<string, string> = {
    analytics: "The current API does not expose analytics, historical aggregates, or performance trends.",
    notifications: "The current API does not expose notifications or alert endpoints.",
  };
  return (
    <div className="page-stack">
      <PageHeading eyebrow="WORKSPACE" title={page.title} description={page.description} />
      <UnavailableState title={`${page.title} data isn’t available`} description={unavailable[section] ?? "This API section is not implemented."} />
    </div>
  );
}

function DataPage({
  title,
  description,
  query,
  emptyTitle,
  emptyCopy,
  icon,
  children,
}: {
  title: string;
  description: string;
  query: { isLoading: boolean; isError: boolean; error: Error | null; refetch: () => unknown; data?: { items: unknown[]; totalCount: number } };
  emptyTitle: string;
  emptyCopy: string;
  icon: React.ReactNode;
  children: React.ReactNode;
}) {
  return (
    <div className="page-stack">
      <PageHeading eyebrow="WORKSPACE" title={title} description={description} />
      <div className="panel list-panel">
        <div className="panel-heading"><div><h2>{title} records</h2><p>{query.data ? `${query.data.items.length} records on this page` : "Live records from the BodaBoss API"}</p></div><span className="live-tag"><span className="online-dot" /> LIVE</span></div>
        {query.isLoading ? <LoadingState label={`Loading ${title.toLowerCase()}`} /> : null}
        {query.isError ? <ErrorState message={query.error?.message ?? "The API request failed."} onRetry={() => void query.refetch()} /> : null}
        {query.data?.items.length === 0 ? <EmptyState title={emptyTitle} description={emptyCopy} icon={icon} /> : null}
        {query.data && query.data.items.length > 0 ? children : null}
        {query.data && query.data.items.length > 0 && <div className="table-footer">Showing {query.data.items.length} of {query.data.totalCount} records <span>First page · maximum 50 records</span></div>}
      </div>
    </div>
  );
}
