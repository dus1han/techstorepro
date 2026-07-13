"use client";

import {
  createContext,
  useCallback,
  useContext,
  useEffect,
  useMemo,
  useState,
  type ReactNode,
} from "react";
import { useRouter } from "next/navigation";
import { api, ApiError } from "@/lib/api-client";
import type {
  AuthResult,
  CurrentUser,
  PermissionAction,
} from "@/types/identity";

/**
 * Session state for the whole app.
 *
 * Tokens live in memory, with only the refresh token persisted to localStorage. That is a
 * deliberate trade rather than an oversight:
 *
 * - the access token is never written to storage, so an XSS payload cannot simply read it out;
 * - the refresh token is persisted, because otherwise every page reload would sign the user out.
 *
 * The genuinely safe answer is an httpOnly, SameSite cookie for the refresh token, which requires
 * the API to set it — a P9 hardening item, recorded in the docs rather than quietly skipped.
 */

const REFRESH_TOKEN_KEY = "techstorepro.refresh";

interface AuthState {
  user: CurrentUser | null;
  accessToken: string | null;
  isLoading: boolean;
  /** The whole login as one string: `ahmed@GULF01`. The company is half of it. */
  login: (login: string, password: string) => Promise<void>;
  logout: () => Promise<void>;
  /** Server-side is the real check; this only decides what to render. */
  can: (feature: string, action: PermissionAction) => boolean;
  /** Restores the session from the stored refresh token. Resolves to whether one was restored. */
  refresh: () => Promise<boolean>;
}

const AuthContext = createContext<AuthState | null>(null);

export function AuthProvider({ children }: { children: ReactNode }) {
  const router = useRouter();

  const [user, setUser] = useState<CurrentUser | null>(null);
  const [accessToken, setAccessToken] = useState<string | null>(null);
  const [isLoading, setIsLoading] = useState(true);

  const apply = useCallback((result: AuthResult) => {
    setAccessToken(result.accessToken);
    localStorage.setItem(REFRESH_TOKEN_KEY, result.refreshToken);
  }, []);

  const clear = useCallback(() => {
    setUser(null);
    setAccessToken(null);
    localStorage.removeItem(REFRESH_TOKEN_KEY);
  }, []);

  /**
   * The permission list deliberately comes from /auth/me, not from the token: the server resolves it
   * per request, so a revoked permission disappears from the UI on the next load rather than
   * lingering until the token expires.
   */
  const loadProfile = useCallback(async (token: string) => {
    const me = await api.get<CurrentUser>("api/v1/auth/me", { token });
    setUser(me);
  }, []);

  const login = useCallback(
    async (login: string, password: string) => {
      // One field: `ahmed@GULF01`. The server splits it — see LoginName.Parse. Sending an `email`
      // here instead would typecheck perfectly and fail at runtime with a validation error, which is
      // precisely the class of drift the OpenAPI codegen exists to kill.
      const result = await api.post<AuthResult>("api/v1/auth/login", {
        body: { login, password },
      });

      apply(result);
      await loadProfile(result.accessToken);
      router.push("/dashboard");
    },
    [apply, loadProfile, router],
  );

  const logout = useCallback(async () => {
    const refreshToken = localStorage.getItem(REFRESH_TOKEN_KEY);

    if (refreshToken) {
      // Best effort. If the call fails the local session is dropped anyway — refusing to sign a
      // user out because the network is down would be absurd.
      await api
        .post("api/v1/auth/logout", { body: { refreshToken } })
        .catch(() => undefined);
    }

    clear();
    router.push("/login");
  }, [clear, router]);

  // There is no switchCompany any more. A user belongs to exactly one company — the company is half
  // of their login — so there is nothing to switch to. Somebody who genuinely works for two companies
  // has two accounts, and signs in as the other one.

  /**
   * Returns whether a session was restored, rather than clearing state itself.
   *
   * That distinction matters to the bootstrap effect below: React (and Next 16's compiler lint)
   * objects to a state update made synchronously from inside an effect, because it can cascade
   * renders. By reporting a result instead of mutating state on the way out, every state change
   * here happens after an await — where it is a normal asynchronous update.
   */
  const refresh = useCallback(async (): Promise<boolean> => {
    const refreshToken = localStorage.getItem(REFRESH_TOKEN_KEY);

    if (!refreshToken) {
      return false;
    }

    try {
      const result = await api.post<AuthResult>("api/v1/auth/refresh", {
        body: { refreshToken },
      });

      apply(result);
      await loadProfile(result.accessToken);

      return true;
    } catch (error) {
      // A rejected refresh token means the session is genuinely over — expired, revoked, or
      // replayed (the API kills the whole chain on replay). Signing out is the correct response.
      if (error instanceof ApiError) {
        return false;
      }

      throw error;
    }
  }, [apply, loadProfile]);

  // Restore the session on first load.
  useEffect(() => {
    let cancelled = false;

    void (async () => {
      const restored = await refresh();

      if (cancelled) {
        return;
      }

      if (!restored) {
        clear();
      }

      setIsLoading(false);
    })();

    return () => {
      cancelled = true;
    };
    // Intentionally once, on mount.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  const can = useCallback(
    (feature: string, action: PermissionAction) => {
      if (!user) return false;

      // The owner holds everything implicitly — the server says so too, so this is not the UI
      // inventing an exception for itself.
      if (user.isOwner) return true;

      return user.permissions.some((p) => p.feature === feature && p.action === action);
    },
    [user],
  );

  const value = useMemo(
    () => ({ user, accessToken, isLoading, login, logout, can, refresh }),
    [user, accessToken, isLoading, login, logout, can, refresh],
  );

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}

export function useAuth(): AuthState {
  const context = useContext(AuthContext);

  if (!context) {
    throw new Error("useAuth must be used inside an <AuthProvider>.");
  }

  return context;
}
