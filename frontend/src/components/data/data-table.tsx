"use client";

import { useEffect, useState, type ReactNode } from "react";
import { useApiQuery } from "@/lib/use-api";
import type { PagedResult } from "@/types/api";

/**
 * The list primitive. Server-side paging, search and sort, driven by the API's PagedResult envelope.
 *
 * Every list screen in the product is this component with a different column set — products,
 * customers, suppliers, invoices, repair jobs, stock movements. It is built once, properly, in P2
 * because roughly forty screens will reuse it, and each one that hand-rolls its own table is a
 * pagination bug waiting to happen.
 *
 * Paging is deliberately server-side. Fetching every product to filter in the browser works
 * beautifully with the fifty rows a demo has, and collapses at the twenty thousand a real importer
 * has.
 */

export interface Column<T> {
  key: string;
  header: string;
  /** Sort key sent to the API. Omit to make the column unsortable. */
  sortBy?: string;
  align?: "left" | "right";
  render: (row: T) => ReactNode;
}

interface DataTableProps<T> {
  /** Cache key root — must be unique per resource. */
  queryKey: readonly unknown[];
  /** API path, e.g. "api/v1/products". */
  endpoint: string;
  columns: Column<T>[];
  filters?: Record<string, string | number | boolean | undefined>;
  searchPlaceholder?: string;
  emptyMessage?: string;
  rowKey: (row: T) => string;
  actions?: (row: T) => ReactNode;
}

export function DataTable<T>({
  queryKey,
  endpoint,
  columns,
  filters,
  searchPlaceholder = "Search…",
  emptyMessage = "Nothing here yet.",
  rowKey,
  actions,
}: DataTableProps<T>) {
  const [search, setSearch] = useState("");
  const [debouncedSearch, setDebouncedSearch] = useState("");
  const [page, setPage] = useState(1);
  const [sortBy, setSortBy] = useState<string | undefined>();
  const [sortDir, setSortDir] = useState<"asc" | "desc">("asc");

  // Debounced: a request per keystroke would hammer the API and race its own responses, leaving the
  // list showing results for a prefix the user has already typed past.
  useEffect(() => {
    const timer = setTimeout(() => {
      setDebouncedSearch(search);
      setPage(1);
    }, 250);

    return () => clearTimeout(timer);
  }, [search]);

  const { data, isPending, error } = useApiQuery<PagedResult<T>>(
    [...queryKey, "list"],
    endpoint,
    {
      page,
      pageSize: 25,
      search: debouncedSearch || undefined,
      sortBy,
      sortDir,
      ...filters,
    },
  );

  function toggleSort(column: Column<T>) {
    if (!column.sortBy) return;

    if (sortBy === column.sortBy) {
      setSortDir((current) => (current === "asc" ? "desc" : "asc"));
      return;
    }

    setSortBy(column.sortBy);
    setSortDir("asc");
  }

  const columnCount = columns.length + (actions ? 1 : 0);

  return (
    <div className="space-y-3">
      <div className="flex items-center justify-between gap-3">
        <input
          value={search}
          onChange={(e) => setSearch(e.target.value)}
          placeholder={searchPlaceholder}
          aria-label="Search"
          className="w-full max-w-xs rounded-md border border-slate-200 bg-transparent px-3 py-1.5 text-sm outline-none focus:border-slate-900 dark:border-slate-700 dark:focus:border-slate-300"
        />

        {data && data.totalCount > 0 && (
          <p className="shrink-0 text-xs tabular-nums text-slate-500">
            {data.totalCount.toLocaleString()} total
          </p>
        )}
      </div>

      {error && (
        <p role="alert" className="rounded-md bg-red-50 px-3 py-2 text-sm text-red-700 dark:bg-red-950 dark:text-red-300">
          {error.message}
        </p>
      )}

      <div className="overflow-x-auto rounded-lg border border-slate-200 dark:border-slate-800">
        <table className="w-full text-sm">
          <thead className="border-b border-slate-200 bg-slate-50 text-left dark:border-slate-800 dark:bg-slate-900">
            <tr>
              {columns.map((column) => (
                <th
                  key={column.key}
                  className={`px-4 py-2.5 font-medium ${column.align === "right" ? "text-right" : ""}`}
                >
                  {column.sortBy ? (
                    <button
                      onClick={() => toggleSort(column)}
                      className="inline-flex items-center gap-1 underline-offset-2 hover:underline"
                    >
                      {column.header}
                      {sortBy === column.sortBy && (
                        <span aria-hidden className="text-slate-400">
                          {sortDir === "asc" ? "▲" : "▼"}
                        </span>
                      )}
                    </button>
                  ) : (
                    column.header
                  )}
                </th>
              ))}
              {actions && <th className="px-4 py-2.5" />}
            </tr>
          </thead>

          <tbody>
            {isPending && (
              <tr>
                <td colSpan={columnCount} className="px-4 py-8 text-center text-slate-500">
                  Loading…
                </td>
              </tr>
            )}

            {data?.items.map((row) => (
              <tr key={rowKey(row)} className="border-b border-slate-100 last:border-0 dark:border-slate-800">
                {columns.map((column) => (
                  <td
                    key={column.key}
                    className={`px-4 py-2.5 ${column.align === "right" ? "text-right tabular-nums" : ""}`}
                  >
                    {column.render(row)}
                  </td>
                ))}
                {actions && <td className="px-4 py-2.5 text-right">{actions(row)}</td>}
              </tr>
            ))}

            {data?.items.length === 0 && (
              <tr>
                <td colSpan={columnCount} className="px-4 py-8 text-center text-slate-500">
                  {debouncedSearch ? `Nothing matches "${debouncedSearch}".` : emptyMessage}
                </td>
              </tr>
            )}
          </tbody>
        </table>
      </div>

      {data && data.totalPages > 1 && (
        <div className="flex items-center justify-between text-sm">
          <p className="text-slate-500">
            Page {data.page} of {data.totalPages}
          </p>

          <div className="flex gap-2">
            <button
              onClick={() => setPage((p) => p - 1)}
              disabled={!data.hasPreviousPage}
              className="rounded-md border border-slate-200 px-2.5 py-1 text-xs disabled:opacity-40 dark:border-slate-700"
            >
              Previous
            </button>
            <button
              onClick={() => setPage((p) => p + 1)}
              disabled={!data.hasNextPage}
              className="rounded-md border border-slate-200 px-2.5 py-1 text-xs disabled:opacity-40 dark:border-slate-700"
            >
              Next
            </button>
          </div>
        </div>
      )}
    </div>
  );
}
