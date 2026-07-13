"use client";

import { useEffect } from "react";
import { useRouter } from "next/navigation";
import { useAuth } from "@/lib/auth-context";

/**
 * The root is a router, not a page: a signed-in user goes to their dashboard, everyone else to the
 * sign-in screen. The P0 landing page that pinged /health has served its purpose — the app has a
 * real front door now, and `/health` is still there for anything that needs to check liveness.
 */
export default function Home() {
  const router = useRouter();
  const { user, isLoading } = useAuth();

  useEffect(() => {
    if (isLoading) return;

    router.replace(user ? "/dashboard" : "/login");
  }, [isLoading, user, router]);

  return (
    <main className="flex min-h-screen items-center justify-center">
      <p className="text-sm text-slate-500">Loading…</p>
    </main>
  );
}
