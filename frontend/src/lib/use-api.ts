"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api } from "@/lib/api-client";
import { useAuth } from "@/lib/auth-context";

/**
 * Typed wrappers over TanStack Query that carry the bearer token for you.
 *
 * The token is part of the cache key, so switching company cannot serve the previous company's
 * cached rows to the new one. That is not a nicety: without it, a company switcher would silently
 * show tenant A's customer list under tenant B's name, and every server-side isolation guarantee
 * would be undone by a cache.
 */

/** GET a resource. Disabled until a token exists, so it never fires an anonymous request. */
export function useApiQuery<T>(
  key: readonly unknown[],
  path: string,
  query?: Record<string, string | number | boolean | undefined>,
  options?: { enabled?: boolean },
) {
  const { accessToken, user } = useAuth();

  return useQuery<T>({
    queryKey: [...key, user?.activeCompanyId ?? null, query ?? null],
    queryFn: () => api.get<T>(path, { token: accessToken!, query }),
    enabled: Boolean(accessToken) && (options?.enabled ?? true),
  });
}

/**
 * POST / PUT / DELETE, invalidating the listed query keys on success so the affected lists refetch.
 * Forgetting the invalidation is the classic bug: the row is created, and the table still shows the
 * old data until the user reloads.
 */
export function useApiMutation<TBody, TResult = unknown>(
  method: "post" | "put" | "delete",
  path: string | ((body: TBody) => string),
  invalidate: readonly unknown[][] = [],
) {
  const { accessToken } = useAuth();
  const client = useQueryClient();

  return useMutation<TResult, Error, TBody>({
    mutationFn: (body: TBody) => {
      const url = typeof path === "function" ? path(body) : path;

      return api[method]<TResult>(url, { token: accessToken!, body });
    },
    onSuccess: async () => {
      await Promise.all(
        invalidate.map((key) => client.invalidateQueries({ queryKey: key })),
      );
    },
  });
}
