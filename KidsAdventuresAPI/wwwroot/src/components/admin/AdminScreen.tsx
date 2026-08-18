import { type ReactNode, useCallback, useEffect, useState } from "react";

import { AdminShell, type AdminNavKey } from "@/components/admin/AdminShell";
import { PasswordlessAuthDialog } from "@/components/auth/PasswordlessAuthDialog";
import * as admin from "@/lib/api/admin";
import { ApiError } from "@/lib/api/client";
import { useAuth } from "@/lib/auth/AuthContext";

/**
 * Everything every admin screen shares: the auth gate, the shell, and the badge counts.
 *
 * The gate matters more than it looks. "Signed in but not an admin" is by far the most
 * common way this page fails, and a blank screen is a terrible way to say so — the 403
 * branch spells out the exact fix, including the part people miss (the role is stamped
 * into the token at issue time, so an existing session stays non-admin until re-issued).
 */
export function AdminScreen({
  active,
  title,
  subtitle,
  actions,
  children,
}: {
  active: AdminNavKey;
  title: string;
  subtitle?: string;
  actions?: ReactNode;
  children: ReactNode;
}) {
  const { isAuthenticated, isLoading, user } = useAuth();
  const [authOpen, setAuthOpen] = useState(false);
  const [counts, setCounts] = useState<Partial<Record<AdminNavKey, number>>>({});

  // The sidebar badges are the same two numbers the overview tiles show, so they are
  // fetched once here rather than by each screen.
  useEffect(() => {
    if (!isAuthenticated) return;
    let cancelled = false;
    void admin
      .getOverview(30)
      .then((o) => {
        if (cancelled) return;
        setCounts({
          production: o.booksFailed + o.booksInFlight,
          fulfillment: o.printOrdersAwaiting,
        });
      })
      .catch(() => {
        /* badges are decoration; a failure here must not blank the screen */
      });
    return () => {
      cancelled = true;
    };
  }, [isAuthenticated]);

  if (isLoading) {
    return (
      <div className="admin-app">
        <div className="app-content">
          <main className="main-content">
            <p className="empty-state">Checking session…</p>
          </main>
        </div>
      </div>
    );
  }

  if (!isAuthenticated) {
    return (
      <div className="admin-app">
        <div className="app-content">
          <main className="main-content">
            <header className="page-heading">
              <div>
                <h1>Beki Admin</h1>
                <p>Sign in with an administrator account to continue.</p>
              </div>
            </header>
            <div className="panel">
              <button
                className="button button-secondary"
                type="button"
                onClick={() => setAuthOpen(true)}
              >
                შესვლა
              </button>
            </div>
          </main>
        </div>
        <PasswordlessAuthDialog
          open={authOpen}
          onOpenChange={setAuthOpen}
          returnPath="/admin"
          onSuccess={() => setAuthOpen(false)}
        />
      </div>
    );
  }

  return (
    <AdminShell
      active={active}
      title={title}
      subtitle={subtitle}
      actions={actions}
      counts={counts}
      operatorName={user?.email ?? user?.phoneNumber ?? undefined}
    >
      {children}
    </AdminShell>
  );
}

/** Load-once helper shared by the data screens. */
export function useAdminData<T>(
  load: () => Promise<T>,
  deps: unknown[],
): { data: T | null; error: string | null; busy: boolean; reload: () => void } {
  const [data, setData] = useState<T | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(true);
  const [nonce, setNonce] = useState(0);

  useEffect(() => {
    let cancelled = false;
    setBusy(true);
    setError(null);
    void load()
      .then((result) => {
        if (!cancelled) setData(result);
      })
      .catch((err: unknown) => {
        if (cancelled) return;
        setError(
          err instanceof ApiError && err.status === 403
            ? "ეს ანგარიში ადმინისტრატორი არ არის. მიანიჭე Users.IsAdmin = 1 და შემდეგ გამოდი და ხელახლა შედი — როლი ტოკენში ჩაიწერება მისი გაცემისას."
            : err instanceof Error
              ? err.message
              : "ჩატვირთვა ვერ მოხერხდა.",
        );
      })
      .finally(() => {
        if (!cancelled) setBusy(false);
      });
    return () => {
      cancelled = true;
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [...deps, nonce]);

  return { data, error, busy, reload: useCallback(() => setNonce((n) => n + 1), []) };
}

/** Consistent loading / error / empty handling inside the approved panel styling. */
export function AdminPanel({
  error,
  busy,
  empty,
  children,
}: {
  error: string | null;
  busy: boolean;
  empty?: boolean;
  children: ReactNode;
}) {
  if (error) {
    return (
      <div className="panel">
        <p className="empty-state">{error}</p>
      </div>
    );
  }
  if (busy) {
    return (
      <div className="panel">
        <p className="empty-state">იტვირთება…</p>
      </div>
    );
  }
  if (empty) {
    return (
      <div className="panel">
        <p className="empty-state">ჯერ არაფერია.</p>
      </div>
    );
  }
  return <>{children}</>;
}

export function statusDot(status: string): string {
  const s = status.toLowerCase();
  if (s === "fulfilled" || s === "completed" || s === "storyready") return "dot dot-success";
  if (s === "failed" || s === "cancelled") return "dot dot-danger";
  return "dot dot-warning";
}
