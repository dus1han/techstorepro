import type { ReactNode } from "react";
import { AppShell } from "@/components/layout/app-shell";

/** Everything under this route group is authenticated and wrapped in the shell. */
export default function AppLayout({ children }: { children: ReactNode }) {
  return <AppShell>{children}</AppShell>;
}
