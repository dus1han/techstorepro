/** Mirrors the RFC 7807 payload produced by the API's ExceptionHandlingMiddleware. */
export interface ProblemDetails {
  type?: string;
  title?: string;
  status?: number;
  detail?: string;
  instance?: string;
  traceId?: string;
  /** Present on validation failures: field name -> messages. */
  errors?: Record<string, string[]>;
}

/** Mirrors PagedResult<T> returned by every list endpoint. */
export interface PagedResult<T> {
  items: T[];
  page: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
  hasNextPage: boolean;
  hasPreviousPage: boolean;
}
