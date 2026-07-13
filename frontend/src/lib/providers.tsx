"use client";

import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { useState, type ReactNode } from "react";
import { AuthProvider } from "@/lib/auth-context";

/**
 * Server state is TanStack Query's job; session state is AuthProvider's.
 *
 * Fetching by hand in a useEffect and pushing the result into useState works until it doesn't: it
 * re-fetches on every render that changes a dependency, races its own responses, and has no cache,
 * so navigating away and back re-downloads everything. It is also what Next 16's React Compiler
 * lint rejects outright. Query solves all of it, and the POS in P5 will need the cache anyway —
 * sub-100ms product lookup is not a round trip per keystroke.
 */
export function Providers({ children }: { children: ReactNode }) {
  // Created once per browser session, inside state, so that a re-render never throws away the cache.
  const [client] = useState(
    () =>
      new QueryClient({
        defaultOptions: {
          queries: {
            // Master data barely changes within a session; re-fetching it on every window focus is
            // noise. The lists that do change are invalidated explicitly after a mutation.
            staleTime: 30_000,
            refetchOnWindowFocus: false,
            // A 401 or 403 will never succeed on retry — the token is not going to become valid by
            // asking again. Only retry what might genuinely be transient.
            retry: (failureCount, error) => {
              const status = (error as { status?: number }).status;

              if (status === 401 || status === 403 || status === 404) {
                return false;
              }

              return failureCount < 2;
            },
          },
        },
      }),
  );

  return (
    <QueryClientProvider client={client}>
      <AuthProvider>{children}</AuthProvider>
    </QueryClientProvider>
  );
}
