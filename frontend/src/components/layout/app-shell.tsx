"use client";

import Link from "next/link";
import { usePathname, useRouter } from "next/navigation";
import { useEffect, type ReactNode } from "react";
import { useAuth } from "@/lib/auth-context";
import { FEATURES, PermissionAction } from "@/types/identity";

/**
 * The authenticated shell: route guard, sidebar, topbar, company switcher.
 *
 * Navigation is filtered by permission, so a user only sees the sections they can actually open.
 * The server enforces the same rules independently — see components/auth/can.tsx.
 */

interface NavItem {
  href: string;
  label: string;
  feature?: string;
}

const NAV: { section: string; items: NavItem[] }[] = [
  {
    section: "Overview",
    items: [{ href: "/dashboard", label: "Dashboard" }],
  },
  {
    section: "Master data",
    items: [
      { href: "/catalog/products", label: "Products", feature: FEATURES.products },
      { href: "/catalog/customers", label: "Customers", feature: FEATURES.customers },
      { href: "/catalog/suppliers", label: "Suppliers", feature: FEATURES.suppliers },
      { href: "/catalog/pricing", label: "Pricing & classification", feature: FEATURES.pricing },
    ],
  },
  {
    section: "Inventory",
    items: [
      { href: "/inventory/stock", label: "Stock on hand", feature: FEATURES.stock },
      { href: "/inventory/movements", label: "Stock movements", feature: FEATURES.stockMovements },
      { href: "/inventory/adjustments", label: "Adjustments", feature: FEATURES.adjustments },
      { href: "/inventory/transfers", label: "Transfers", feature: FEATURES.transfers },
      { href: "/inventory/counts", label: "Stock counts", feature: FEATURES.stockCounts },
      { href: "/inventory/serials", label: "Serial numbers", feature: FEATURES.serials },
    ],
  },
  {
    section: "Settings",
    items: [
      { href: "/settings/branches", label: "Branches", feature: FEATURES.branches },
      { href: "/settings/warehouses", label: "Warehouses", feature: FEATURES.warehouses },
      { href: "/settings/users", label: "Users & permissions", feature: FEATURES.users },
      { href: "/settings/configuration", label: "Configuration", feature: FEATURES.settings },
    ],
  },
];

export function AppShell({ children }: { children: ReactNode }) {
  const { user, isLoading, logout } = useAuth();
  const router = useRouter();
  const pathname = usePathname();

  // The route guard. This is a convenience, not a security boundary: the data behind every page
  // comes from the API, which refuses an unauthenticated request regardless of what the browser
  // decided to render.
  useEffect(() => {
    if (!isLoading && !user) {
      router.replace("/login");
    }
  }, [isLoading, user, router]);

  if (isLoading) {
    return (
      <div className="flex min-h-screen items-center justify-center">
        <p className="text-sm text-slate-500">Loading…</p>
      </div>
    );
  }

  if (!user) {
    return null;
  }

  return (
    <div className="flex min-h-screen">
      <aside className="hidden w-64 shrink-0 border-r border-slate-200 bg-slate-50 md:block dark:border-slate-800 dark:bg-slate-900">
        <div className="flex h-14 items-center border-b border-slate-200 px-5 dark:border-slate-800">
          <span className="font-semibold tracking-tight">TechStorePro</span>
        </div>

        <nav className="space-y-6 p-4">
          {NAV.map((group) => {
            const visible = group.items.filter(
              (item) => !item.feature || permitted(item.feature),
            );

            if (visible.length === 0) return null;

            return (
              <div key={group.section}>
                <p className="px-2 pb-2 text-xs font-medium uppercase tracking-wider text-slate-400">
                  {group.section}
                </p>
                <ul className="space-y-0.5">
                  {visible.map((item) => (
                    <li key={item.href}>
                      <Link
                        href={item.href}
                        className={`block rounded-md px-2 py-1.5 text-sm transition-colors ${
                          pathname === item.href
                            ? "bg-slate-900 text-white dark:bg-slate-100 dark:text-slate-900"
                            : "text-slate-700 hover:bg-slate-200 dark:text-slate-300 dark:hover:bg-slate-800"
                        }`}
                      >
                        {item.label}
                      </Link>
                    </li>
                  ))}
                </ul>
              </div>
            );
          })}
        </nav>
      </aside>

      <div className="flex min-w-0 flex-1 flex-col">
        <header className="flex h-14 items-center justify-between gap-4 border-b border-slate-200 px-6 dark:border-slate-800">
          <CompanyBadge />

          <div className="flex items-center gap-4">
            <div className="text-right">
              <p className="text-sm font-medium leading-tight">{user.fullName}</p>
              <p className="text-xs leading-tight text-slate-500">
                {user.isOwner ? "Owner" : `${user.permissions.length} permissions`}
              </p>
            </div>
            <button
              onClick={() => void logout()}
              className="rounded-md border border-slate-200 px-3 py-1.5 text-sm hover:bg-slate-100 dark:border-slate-700 dark:hover:bg-slate-800"
            >
              Sign out
            </button>
          </div>
        </header>

        <main className="min-w-0 flex-1 p-6">{children}</main>
      </div>
    </div>
  );

  function permitted(feature: string) {
    // Seeing a section at all requires View on it.
    return user!.isOwner
      || user!.permissions.some(
        (p) => p.feature === feature && p.action === PermissionAction.View,
      );
  }
}

/**
 * The company the user works for, shown rather than chosen.
 *
 * This used to be a switcher. It cannot be one any more: a user belongs to exactly one company —
 * the company is half of their login — so there is nothing to switch between. Somebody who genuinely
 * works for two companies has two accounts and signs in as the other one.
 */
function CompanyBadge() {
  const { user } = useAuth();

  if (!user) {
    return <p className="text-sm font-medium">—</p>;
  }

  return (
    <div className="text-sm">
      <p className="font-medium">{user.companyName ?? "—"}</p>
      {user.companyCode && (
        // Worth showing: it is the half of their login they are most likely to forget, and the one
        // thing they cannot look up anywhere else.
        <p className="font-mono text-xs text-slate-500">
          {user.username}@{user.companyCode}
        </p>
      )}
    </div>
  );
}
