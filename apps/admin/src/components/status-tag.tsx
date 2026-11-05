export function StatusTag({ value }: { value: string }) {
  const normalized = value.toLowerCase();
  const kind = ["available", "active", "recorded", "completed"].includes(normalized)
    ? "status-success"
    : ["inprogress", "in progress"].includes(normalized)
      ? "status-info"
      : ["maintenance", "inactive", "failed"].includes(normalized)
        ? "status-warning"
        : "status-neutral";
  return <span className={`status-tag ${kind}`}><span />{value}</span>;
}
