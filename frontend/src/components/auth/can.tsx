"use client";

import type { ReactNode } from "react";
import { useAuth } from "@/lib/auth-context";
import type { PermissionAction } from "@/types/identity";

/**
 * Renders its children only if the current user holds (feature, action).
 *
 * This is **cosmetic**. Hiding a button does not protect anything — the endpoint behind it is still
 * reachable with curl. The real check is PermissionBehaviour in the API pipeline, which runs on
 * every command regardless of what the UI chose to draw. This exists so users are not shown doors
 * that will slam in their face, not to keep anyone out.
 */
export function Can({
  feature,
  action,
  children,
  fallback = null,
}: {
  feature: string;
  action: PermissionAction;
  children: ReactNode;
  fallback?: ReactNode;
}) {
  const { can } = useAuth();

  return can(feature, action) ? <>{children}</> : <>{fallback}</>;
}
