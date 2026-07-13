"use client";

import { useState } from "react";
import { ApiError } from "@/lib/api-client";
import { useAuth } from "@/lib/auth-context";

export default function LoginPage() {
  const { login } = useAuth();

  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  async function onSubmit(event: React.FormEvent) {
    event.preventDefault();
    setError(null);
    setBusy(true);

    try {
      await login(email, password);
    } catch (caught) {
      // The API deliberately gives the same message for "no such user" and "wrong password", so
      // this cannot be used to discover which email addresses are registered. Passing its message
      // through unchanged keeps that property.
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
            <label htmlFor="email" className="text-sm font-medium">
              Email
            </label>
            <input
              id="email"
              type="email"
              autoComplete="username"
              required
              value={email}
              onChange={(e) => setEmail(e.target.value)}
              className="w-full rounded-md border border-slate-200 bg-transparent px-3 py-2 text-sm outline-none focus:border-slate-900 dark:border-slate-700 dark:focus:border-slate-300"
            />
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
      </div>
    </main>
  );
}
