"use client";

import { useState } from "react";
import { ApiError } from "@/lib/api-client";
import { useAuth } from "@/lib/auth-context";

/**
 * One field, not two.
 *
 * A username is unique only within a company — two shops may each have an "admin" — so the login has
 * to say which company it means. It could have been a separate "company code" box, but that asks the
 * user to know something they have no way of discovering; and it could have been a dropdown, but that
 * would show every tenant on the platform to anyone who opened this page.
 *
 * So the company rides along in the login itself: `ahmed@GULF01`. The user learns their own code once,
 * from whoever set up their account, and never needs to know that any other company exists.
 */
export default function LoginPage() {
  const { login } = useAuth();

  const [loginName, setLoginName] = useState("");
  const [password, setPassword] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  async function onSubmit(event: React.FormEvent) {
    event.preventDefault();
    setError(null);
    setBusy(true);

    try {
      await login(loginName, password);
    } catch (caught) {
      // The API deliberately gives the same message for "no such company", "no such user" and "wrong
      // password". Told apart they would be a map of the platform, so passing its message through
      // unchanged is what keeps that property — do not try to be more helpful here.
      setError(
        caught instanceof ApiError
          ? caught.message
          : "Could not reach the server. Is the API running?",
      );
    } finally {
      setBusy(false);
    }
  }

  return (
    <main className="flex min-h-screen items-center justify-center px-6">
      <div className="w-full max-w-sm">
        <div className="mb-8 space-y-1">
          <h1 className="text-2xl font-semibold tracking-tight">TechStorePro</h1>
          <p className="text-sm text-slate-500">Sign in to your company.</p>
        </div>

        <form onSubmit={onSubmit} className="space-y-4">
          <div className="space-y-1.5">
            <label htmlFor="login" className="text-sm font-medium">
              Username
            </label>
            <input
              id="login"
              type="text"
              inputMode="email"
              autoComplete="username"
              autoCapitalize="none"
              spellCheck={false}
              required
              placeholder="ahmed@GULF01"
              value={loginName}
              onChange={(e) => setLoginName(e.target.value)}
              className="w-full rounded-md border border-slate-200 bg-transparent px-3 py-2 text-sm outline-none focus:border-slate-900 dark:border-slate-700 dark:focus:border-slate-300"
            />
            <p className="text-xs text-slate-500">
              Your username, then your company code — for example{" "}
              <span className="font-mono">ahmed@GULF01</span>.
            </p>
          </div>

          <div className="space-y-1.5">
            <label htmlFor="password" className="text-sm font-medium">
              Password
            </label>
            <input
              id="password"
              type="password"
              autoComplete="current-password"
              required
              value={password}
              onChange={(e) => setPassword(e.target.value)}
              className="w-full rounded-md border border-slate-200 bg-transparent px-3 py-2 text-sm outline-none focus:border-slate-900 dark:border-slate-700 dark:focus:border-slate-300"
            />
          </div>

          {error && (
            <p
              role="alert"
              className="rounded-md bg-red-50 px-3 py-2 text-sm text-red-700 dark:bg-red-950 dark:text-red-300"
            >
              {error}
            </p>
          )}

          <button
            type="submit"
            disabled={busy}
            className="w-full rounded-md bg-slate-900 px-3 py-2 text-sm font-medium text-white transition-opacity hover:opacity-90 disabled:opacity-50 dark:bg-slate-100 dark:text-slate-900"
          >
            {busy ? "Signing in…" : "Sign in"}
          </button>
        </form>

        {/* No "create an account" link, and that is not an omission. A company cannot bring itself
            into existence any more — TechStorePro onboards it, and hands over the first login. */}
        <p className="mt-6 text-center text-xs text-slate-500">
          Need an account? Your company administrator creates it for you.
        </p>
      </div>
    </main>
  );
}
