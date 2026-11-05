"use client";

import {
  Activity,
  Bike,
  BriefcaseBusiness,
  ChartNoAxesCombined,
  CircleDollarSign,
  ClipboardList,
  Gauge,
  LayoutDashboard,
  LogOut,
  Menu,
  Radio,
  Settings,
  UsersRound,
  X,
  type LucideIcon,
} from "lucide-react";
import { useQueryClient } from "@tanstack/react-query";
import Link from "next/link";
import { usePathname, useRouter } from "next/navigation";
import { useEffect, useState } from "react";
import { canAccessSection, useAuth } from "@/lib/auth";

interface NavItem {
  label: string;
  href: string;
  section: string;
  icon: LucideIcon;
}

const navItems: NavItem[] = [
  { label: "Overview", href: "/", section: "overview", icon: LayoutDashboard },
  { label: "Riders", href: "/riders", section: "riders", icon: UsersRound },
  { label: "Motorcycles", href: "/motorcycles", section: "motorcycles", icon: Bike },
  { label: "Jobs", href: "/jobs", section: "jobs", icon: BriefcaseBusiness },
  { label: "Payments", href: "/payments", section: "payments", icon: CircleDollarSign },
  { label: "Reports", href: "/reports", section: "reports", icon: ClipboardList },
  { label: "Maintenance", href: "/maintenance", section: "maintenance", icon: Settings },
  { label: "GPS tracking", href: "/gps", section: "gps", icon: Radio },
  { label: "Analytics", href: "/analytics", section: "analytics", icon: ChartNoAxesCombined },
  { label: "Notifications", href: "/notifications", section: "notifications", icon: Activity },
];

export function AppShell({ children }: { children: React.ReactNode }) {
  const { session, signOut } = useAuth();
  const pathname = usePathname();
  const router = useRouter();
  const queryClient = useQueryClient();
  const [mobileOpen, setMobileOpen] = useState(false);
  const visibleItems = navItems.filter((item) =>
    canAccessSection(session?.role ?? "", item.section),
  );

  function exit() {
    queryClient.clear();
    signOut();
    router.replace("/login");
  }

  return (
    <div className="app-frame">
      {mobileOpen && (
        <button
          aria-label="Close navigation"
          className="mobile-backdrop"
          onClick={() => setMobileOpen(false)}
        />
      )}
      <aside className={`sidebar ${mobileOpen ? "sidebar-open" : ""}`}>
        <div className="brand">
          <div className="brand-mark"><Bike size={21} strokeWidth={2.2} /></div>
          <div><strong>BodaBoss</strong><span>ADMIN PORTAL</span></div>
          <button
            aria-label="Close navigation"
            className="icon-button mobile-close"
            onClick={() => setMobileOpen(false)}
          ><X size={19} /></button>
        </div>
        <div className="workspace-pill"><span className="online-dot" /> Fleet workspace</div>
        <nav className="nav-list" aria-label="Main navigation">
          <p className="nav-caption">WORKSPACE</p>
          {visibleItems.map(({ label, href, icon: Icon }) => {
            const active = href === "/" ? pathname === "/" : pathname.startsWith(href);
            return (
              <Link
                key={href}
                href={href}
                className={`nav-link ${active ? "nav-link-active" : ""}`}
                onClick={() => setMobileOpen(false)}
              >
                <Icon size={18} strokeWidth={1.9} /><span>{label}</span>
                {active && <span className="active-indicator" />}
              </Link>
            );
          })}
        </nav>
        <div className="sidebar-bottom">
          <div className="support-card">
            <div className="support-icon"><Gauge size={17} /></div>
            <strong>Fleet operations</strong>
            <span>Manage your business from one place.</span>
          </div>
          <div className="account-row">
            <div className="avatar">{(session?.email?.[0] ?? "A").toUpperCase()}</div>
            <div className="account-copy">
              <strong>{session?.email ?? "Administrator"}</strong>
              <span>{session?.role ?? "Admin"}</span>
            </div>
            <button aria-label="Sign out" className="icon-button" onClick={exit} title="Sign out">
              <LogOut size={17} />
            </button>
          </div>
        </div>
      </aside>
      <div className="main-column">
        <header className="topbar">
          <button
            aria-label="Open navigation"
            className="icon-button mobile-menu"
            onClick={() => setMobileOpen(true)}
          ><Menu size={21} /></button>
          <div className="topbar-label"><span className="topbar-kicker">BODABOSS</span><span className="topbar-divider">/</span><span>Admin</span></div>
          <div className="topbar-right">
            <span className="api-indicator"><span className="online-dot" /> Secured workspace</span>
            <div className="avatar avatar-small">{(session?.email?.[0] ?? "A").toUpperCase()}</div>
          </div>
        </header>
        <main className="main-content">{children}</main>
      </div>
    </div>
  );
}

export function PageHeading({
  eyebrow,
  title,
  description,
  action,
}: {
  eyebrow?: string;
  title: string;
  description: string;
  action?: React.ReactNode;
}) {
  return (
    <div className="page-heading">
      <div>
        {eyebrow && <p className="eyebrow">{eyebrow}</p>}
        <h1>{title}</h1>
        <p className="page-description">{description}</p>
      </div>
      {action}
    </div>
  );
}

export function PageGuard({ children }: { children: React.ReactNode }) {
  const { session, ready } = useAuth();
  const router = useRouter();

  useEffect(() => {
    if (ready && !session) router.replace("/login");
  }, [ready, session, router]);

  if (!ready) return <div className="app-loading"><span className="spinner" /> Loading workspace</div>;
  if (!session) {
    return <div className="app-loading"><span className="spinner" /> Checking your session</div>;
  }
  return <AppShell>{children}</AppShell>;
}
