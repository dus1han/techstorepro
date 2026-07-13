import { env } from "@/lib/env";
import type { ProblemDetails } from "@/types/api";

/**
 * Thrown for any non-2xx response. Carries the parsed problem details so callers can show
 * field-level validation messages without re-parsing the body.
 */
export class ApiError extends Error {
  constructor(
    readonly status: number,
    readonly problem: ProblemDetails,
  ) {
    super(problem.detail ?? problem.title ?? `Request failed with status ${status}`);
    this.name = "ApiError";
  }

  /** Field name -> messages, empty when the failure was not a validation error. */
  get fieldErrors(): Record<string, string[]> {
    return this.problem.errors ?? {};
  }
}

type RequestOptions = Omit<RequestInit, "body" | "method"> & {
  /** Bearer token. The company the request acts on is carried inside this token's claims. */
  token?: string;
  body?: unknown;
  query?: Record<string, string | number | boolean | undefined>;
};

async function request<T>(
  method: string,
  path: string,
  { token, body, query, headers, ...init }: RequestOptions = {},
): Promise<T> {
  const url = new URL(path.replace(/^\//, ""), `${env.apiBaseUrl.replace(/\/$/, "")}/`);

  for (const [key, value] of Object.entries(query ?? {})) {
    if (value !== undefined) url.searchParams.set(key, String(value));
  }

  const response = await fetch(url, {
    ...init,
    method,
    headers: {
      Accept: "application/json",
      ...(body !== undefined ? { "Content-Type": "application/json" } : {}),
      ...(token ? { Authorization: `Bearer ${token}` } : {}),
      ...headers,
    },
    body: body !== undefined ? JSON.stringify(body) : undefined,
  });

  if (!response.ok) {
    let problem: ProblemDetails = { status: response.status, title: response.statusText };
    try {
      problem = { ...problem, ...(await response.json()) };
    } catch {
      // Body was empty or not JSON; the status-derived problem above is the best we have.
    }
    throw new ApiError(response.status, problem);
  }

  if (response.status === 204) {
    return undefined as T;
  }

  return (await response.json()) as T;
}

export const api = {
  get: <T>(path: string, options?: RequestOptions) => request<T>("GET", path, options),
  post: <T>(path: string, options?: RequestOptions) => request<T>("POST", path, options),
  put: <T>(path: string, options?: RequestOptions) => request<T>("PUT", path, options),
  patch: <T>(path: string, options?: RequestOptions) => request<T>("PATCH", path, options),
  delete: <T>(path: string, options?: RequestOptions) => request<T>("DELETE", path, options),
};
