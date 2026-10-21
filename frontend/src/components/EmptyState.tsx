import type { ReactNode } from "react";

export function EmptyState({ title, children }: { title: string; children?: ReactNode }) {
  return (
    <div className="rounded-xl border border-dashed border-line bg-surface px-6 py-12 text-center">
      <p className="font-display text-xl font-bold">{title}</p>
      {children && <div className="mx-auto mt-2 max-w-[50ch] text-muted">{children}</div>}
    </div>
  );
}
