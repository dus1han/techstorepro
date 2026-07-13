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
          <CompanySwitcher />

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
 * Switching company re-authenticates: the API mints a new token carrying the new company_id claim.
 * The tenant is never a header or a dropdown value the client sends with each request — that could
 * be edited to point at a company the user has no membership of.
 */
function CompanySwitcher() {
  const { companies, user, switchCompany } = useAuth();

  if (companies.length <= 1) {
    return (
      <p className="text-sm font-medium">{user?.activeCompanyName ?? "—"}</p>
    );
  }

  return (
    <label className="flex items-center gap-2 text-sm">
      <span className="text-slate-500">Company</span>
      <select
        value={user?.activeCompanyId ?? ""}
        onChange={(event) => void switchCompany(event.target.value)}
        className="rounded-md border border-slate-200 bg-transparent px-2 py-1.5 text-sm dark:border-slate-700"
      >
        {companies.map((company) => (
          <option key={company.companyId} value={company.companyId}>
            {company.companyName}
          </option>
        ))}
      </select>
    </label>
  );
}
