"use client";

import { useQuery } from "@tanstack/react-query";
import { Activity, ArrowDownRight, ArrowUpRight, Bike, CircleDollarSign, Clock3, Route, UsersRound, type LucideIcon } from "lucide-react";
import Link from "next/link";
import { canAccessSection, useAuth } from "@/lib/auth";
import { api } from "@/lib/api";
import { PageGuard, PageHeading } from "@/components/app-shell";
import { EmptyState, ErrorState, LoadingState } from "@/components/data-state";
import { StatusTag } from "@/components/status-tag";

const numberFormat = new Intl.NumberFormat("en-UG");
const currencyFormat = new Intl.NumberFormat("en-UG", {
  style: "currency",
  currency: "UGX",
  maximumFractionDigits: 0,
});

export default function OverviewPage() {
  return (
    <PageGuard>
      <Overview />
    </PageGuard>
  );
}

function Overview() {
  const { session } = useAuth();
  const canSeePayments = session ? canAccessSection(session.role, "payments") : false;
  const summaryQuery = useQuery({
    queryKey: ["dashboard", session?.accessToken],
    queryFn: () => api.dashboard(session!.accessToken),
    enabled: Boolean(session),
    refetchInterval: 30_000,
  });
  const summary = summaryQuery.data;
  const today = new Date().toLocaleDateString("en-UG", {
    weekday: "long",
    month: "long",
    day: "numeric",
    timeZone: "Africa/Kampala",
  }).toUpperCase();
  const hour = Number(new Date().toLocaleString("en-UG", {
    hour: "2-digit",
    hour12: false,
    timeZone: "Africa/Kampala",
  }));
  const greeting = hour < 12 ? "Good morning" : hour < 17 ? "Good afternoon" : "Good evening";
  const stats = summary
    ? [
        { label: "Total motorcycles", value: numberFormat.format(summary.fleetCount), icon: Bike, color: "green", foot: "Registered in fleet" },
        { label: "Available now", value: numberFormat.format(summary.availableCount), icon: UsersRound, color: "blue", foot: "Ready for assignment" },
        { label: "Active jobs", value: numberFormat.format(summary.activeTrips), icon: Route, color: "orange", foot: "Currently in progress" },
        canSeePayments
          ? { label: "Revenue today", value: currencyFormat.format(summary.revenueTodayUgx), icon: CircleDollarSign, color: "violet", foot: "Recorded payments" }
          : { label: "Motorcycles tracked", value: numberFormat.format(summary.trackedCount), icon: Activity, color: "violet", foot: "Location reported in 5 min" },
      ]
    : [];

  return (
    <div className="page-stack">
      <PageHeading
        eyebrow={today}
        title={greeting}
        description="Here’s what’s happening across your fleet today."
        action={<span className="date-chip"><Clock3 size={15} /> Live overview</span>}
      />
      {summaryQuery.isLoading ? (
        <LoadingState label="Loading your dashboard" />
      ) : summaryQuery.isError ? (
        <ErrorState
          message={summaryQuery.error.message}
          onRetry={() => void summaryQuery.refetch()}
        />
      ) : summary ? (
        <>
          <section className="stat-grid" aria-label="Fleet summary">
            {stats.map((stat) => <StatCard key={stat.label} {...stat} />)}
          </section>
          <section className="dashboard-grid">
            {canSeePayments && <div className="panel recent-panel">
              <div className="panel-heading">
                <div><h2>Recent payments</h2><p>Latest records from your account</p></div>
                <Link className="text-link" href="/payments">View payments <ArrowUpRight size={14} /></Link>
              </div>
              {summary.recentPayments.length ? (
                <div className="table-scroll">
                  <table>
                    <thead><tr><th>Payment</th><th>Method</th><th>Date received</th><th>Amount</th><th>Status</th></tr></thead>
                    <tbody>{summary.recentPayments.map((payment) => (
                      <tr key={payment.id}>
                        <td><span className="record-name"><span className="record-avatar payment-avatar"><CircleDollarSign size={15} /></span><span><strong>Payment</strong><small>#{payment.id.slice(0, 8)}</small></span></span></td>
                        <td>{payment.method}</td>
                        <td>{new Date(payment.receivedAt).toLocaleString("en-UG", { dateStyle: "medium", timeStyle: "short" })}</td>
                        <td className="amount-cell">{currencyFormat.format(payment.amountUgx)}</td>
                        <td><StatusTag value={payment.status} /></td>
                      </tr>
                    ))}</tbody>
                  </table>
                </div>
              ) : (
                <EmptyState title="No payments yet" description="Recorded payments will appear here when available from your account." icon={<CircleDollarSign size={20} />} />
              )}
            </div>}
            <div className={`panel activity-panel ${canSeePayments ? "" : "activity-wide"}`}>
              <div className="panel-heading"><div><h2>Fleet activity</h2><p>Current operational snapshot</p></div><Activity size={18} className="muted-icon" /></div>
              <div className="activity-row">
                <div className="activity-icon green-soft"><Bike size={17} /></div>
                <div className="activity-copy"><strong>Motorcycles tracked</strong><span>Location reported in the last 5 minutes</span></div>
                <b>{numberFormat.format(summary.trackedCount)}</b>
              </div>
              <div className="activity-row">
                <div className="activity-icon blue-soft"><Route size={17} /></div>
                <div className="activity-copy"><strong>Jobs in progress</strong><span>Live count from the operations API</span></div>
                <b>{numberFormat.format(summary.activeTrips)}</b>
              </div>
              <div className="activity-footnote"><span className="online-dot" /> Values are returned by the BodaBoss API</div>
            </div>
          </section>
          <div className="notice-banner">
            <div className="notice-mark"><Activity size={17} /></div>
            <div><strong>Operational data, not estimates</strong><span>This overview only shows figures returned by your API. No sample transactions or simulated GPS data are displayed.</span></div>
          </div>
        </>
      ) : null}
    </div>
  );
}

function StatCard({
  label,
  value,
  icon: Icon,
  color,
  foot,
}: {
  label: string;
  value: string;
  icon: LucideIcon;
  color: string;
  foot: string;
}) {
  return (
    <div className="stat-card">
      <div className="stat-top"><span>{label}</span><span className={`stat-icon ${color}`}><Icon size={18} /></span></div>
      <strong className="stat-value">{value}</strong>
      <div className="stat-foot"><span className="stat-foot-icon"><ArrowDownRight size={13} /></span>{foot}</div>
    </div>
  );
}
