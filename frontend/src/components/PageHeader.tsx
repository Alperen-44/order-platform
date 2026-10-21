import type { ReactNode } from "react";

export function PageHeader({ title, children }: { title: string; children?: ReactNode }) {
  return (
    <div className="mb-8">
      <h1 className="font-display text-4xl font-bold tracking-tight sm:text-5xl">{title}</h1>
      {children && <div className="mt-3 max-w-[60ch] text-[17px] leading-relaxed text-muted">{children}</div>}
    </div>
  );
}
