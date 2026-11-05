"use client";

import {
  createContext,
  useCallback,
  useContext,
  useEffect,
  useMemo,
  useState,
} from "react";
import { api } from "@/lib/api";
import type { Session, UserRole } from "@/lib/types";

interface AuthContextValue {
  session: Session | null;
  ready: boolean;
  signIn: (credentials: {
    tenantId: string;
    identifier: string;
    password: string;
  }) => Promise<void>;
  signOut: () => void;
}

const AuthContext = createContext<AuthContextValue | null>(null);
const STORAGE_KEY = "bodaboss.admin.session";

export function AuthProvider({ children }: { children: React.ReactNode }) {
  const [session, setSession] = useState<Session | null>(null);
  const [ready, setReady] = useState(false);

  useEffect(() => {
    try {
      const stored = localStorage.getItem(STORAGE_KEY);
      if (stored) setSession(JSON.parse(stored) as Session);
    } catch {
      localStorage.removeItem(STORAGE_KEY);
    } finally {
      setReady(true);
    }
  }, []);

  const signIn = useCallback(
    async (credentials: { tenantId: string; identifier: string; password: string }) => {
      const result = await api.login(credentials);
      if (!["SuperAdmin", "OperationsManager", "FinanceOfficer"].includes(result.role)) {
        throw new Error("This account does not have admin-portal access.");
      }
      const authenticated: Session = {
        accessToken: result.accessToken,
        refreshToken: result.refreshToken,
        expiresAt: result.expiresAt,
        email: result.email,
        role: result.role as UserRole,
      };
      localStorage.setItem(STORAGE_KEY, JSON.stringify(authenticated));
      setSession(authenticated);
    },
    [],
  );

  const signOut = useCallback(() => {
    localStorage.removeItem(STORAGE_KEY);
    setSession(null);
  }, []);

  const value = useMemo(
    () => ({ session, ready, signIn, signOut }),
    [session, ready, signIn, signOut],
  );

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}

export function useAuth() {
  const context = useContext(AuthContext);
  if (!context) throw new Error("useAuth must be used inside AuthProvider.");
  return context;
}

export function canAccessSection(role: UserRole, section: string): boolean {
  const normalized = role.toLowerCase().replace(/[\s_-]/g, "");
  if (normalized === "admin" || normalized === "superadmin") return true;
  if (normalized === "operationsmanager") {
    return [
      "overview",
      "riders",
      "motorcycles",
      "jobs",
      "maintenance",
      "gps",
      "notifications",
    ].includes(section);
  }
  if (normalized === "financeofficer") {
    return ["overview", "payments", "reports", "analytics", "notifications"].includes(
      section,
    );
  }
  return section === "overview";
}
