"use client";

import { AlertCircle, RefreshCw } from "lucide-react";
import type { ReactNode } from "react";

export function LoadingState({ label = "Loading live data" }: { label?: string }) {
  return (
    <div className="state-card">
      <span className="spinner" />
      <strong>{label}</strong>
      <p>Connecting to the BodaBoss API…</p>
    </div>
  );
}

export function ErrorState({
  message,
  onRetry,
}: {
  message: string;
  onRetry: () => void;
}) {
  return (
    <div className="state-card state-error">
      <div className="state-icon error-icon"><AlertCircle size={21} /></div>
      <strong>Couldn’t load this data</strong>
      <p>{message}</p>
      <button className="button button-secondary button-small" onClick={onRetry}>
        <RefreshCw size={15} /> Try again
      </button>
    </div>
  );
}

export function EmptyState({
  title,
  description,
  icon,
}: {
  title: string;
  description: string;
  icon?: ReactNode;
}) {
  return (
    <div className="empty-state">
      <div className="empty-icon">{icon}</div>
      <strong>{title}</strong>
      <p>{description}</p>
    </div>
  );
}

export function UnavailableState({
  title,
  description,
}: {
  title: string;
  description: string;
}) {
  return (
    <div className="unavailable-card">
      <div className="unavailable-art"><span /><span /><span /></div>
      <span className="status-tag status-neutral">NOT AVAILABLE YET</span>
      <h2>{title}</h2>
      <p>{description}</p>
      <div className="unavailable-note">
        This section is intentionally empty until the BodaBoss API provides this data.
      </div>
    </div>
  );
}
