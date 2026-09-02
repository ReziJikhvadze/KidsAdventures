import { type ReactNode, useCallback, useEffect, useRef, useState } from "react";

import { AdminShell, type AdminNavKey } from "@/components/admin/AdminShell";
import { PasswordlessAuthDialog } from "@/components/auth/PasswordlessAuthDialog";
import { ApiError } from "@/lib/api/client";
import { useAuth } from "@/lib/auth/AuthContext";

/**
 * Everything every admin screen shares: the auth gate and the shell.
 *
 * The gate matters more than it looks. "Signed in but not an admin" is by far the most
 * common way this page fails, and a blank screen is a terrible way to say so — the branch
 * spells out the exact fix, including the part people miss (the role is stamped into the token
 * at issue time, so an existing session stays non-admin until re-issued). It is decided here,
 * once, from the session, instead of by every panel receiving its own 403.
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

  if (isLoading) {
    return (
      <div className="admin-app">
        <div className="app-content">
          <main className="main-content">
            <p className="empty-state">სესია მოწმდება…</p>
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
                <p>გასაგრძელებლად შედი ადმინისტრატორის ანგარიშით.</p>
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

  if (user && !user.isAdmin) {
    return (
      <div className="admin-app">
        <div className="app-content">
          <main className="main-content">
            <header className="page-heading">
              <div>
                <h1>Beki Admin</h1>
                <p>ეს ანგარიში ადმინისტრატორი არ არის.</p>
              </div>
            </header>
            <div className="panel">
              <p className="empty-state">
                მიანიჭე ამ ანგარიშს ადმინის როლი (Users.IsAdmin = 1 ან „ადმინისტრატორები“ გვერდიდან)
                და შემდეგ გამოდი და ხელახლა შედი — როლი ტოკენში მისი გაცემისას ჩაიწერება.
              </p>
            </div>
          </main>
        </div>
      </div>
    );
  }

  return (
    <AdminShell
      active={active}
      title={title}
      subtitle={subtitle}
      actions={actions}
      operatorName={user?.email ?? user?.phoneNumber ?? undefined}
    >
      {children}
    </AdminShell>
  );
}

/**
 * Load-and-reload helper shared by the data screens.
 *
 * The last answer stays on screen while a new one is fetched. This used to blank the table on
 * every reload, which meant every action — approving a book, downloading a file, typing one
 * letter into the search box — unmounted the list, closed the open row and threw away the
 * notice the action had just produced. `busy` is now true only while there is nothing to show;
 * `refreshing` says a newer answer is on its way.
 */
export function useAdminData<T>(
  load: () => Promise<T>,
  deps: unknown[],
): {
  data: T | null;
  error: string | null;
  busy: boolean;
  refreshing: boolean;
  reload: () => void;
} {
  const [data, setData] = useState<T | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(true);
  const [refreshing, setRefreshing] = useState(false);
  const [nonce, setNonce] = useState(0);
  const hasData = useRef(false);

  useEffect(() => {
    let cancelled = false;
    if (hasData.current) setRefreshing(true);
    else setBusy(true);
    setError(null);
    void load()
      .then((result) => {
        if (cancelled) return;
        hasData.current = true;
        setData(result);
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
        if (cancelled) return;
        setBusy(false);
        setRefreshing(false);
      });
    return () => {
      cancelled = true;
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [...deps, nonce]);

  return {
    data,
    error,
    busy,
    refreshing,
    reload: useCallback(() => setNonce((n) => n + 1), []),
  };
}

/** A value that follows its input after the typing pauses, for search boxes. */
export function useDebounced<T>(value: T, delayMs = 300): T {
  const [debounced, setDebounced] = useState(value);
  useEffect(() => {
    const timer = window.setTimeout(() => setDebounced(value), delayMs);
    return () => window.clearTimeout(timer);
  }, [value, delayMs]);
  return debounced;
}

/** Consistent loading / error / empty handling inside the approved panel styling. */
export function AdminPanel({
  error,
  busy,
  empty,
  emptyText,
  children,
}: {
  error: string | null;
  busy: boolean;
  empty?: boolean;
  emptyText?: string;
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
        <p className="empty-state">{emptyText ?? "ჯერ არაფერია."}</p>
      </div>
    );
  }
  return <>{children}</>;
}

export function statusDot(status: string | null | undefined): string {
  const s = (status ?? "").toLowerCase();
  if (s === "fulfilled" || s === "completed" || s === "storyready" || s === "delivered")
    return "dot dot-success";
  if (s === "failed" || s === "cancelled" || s === "refunded") return "dot dot-danger";
  return "dot dot-warning";
}

/**
 * A yes/no question before something that costs money or cannot be undone.
 *
 * `window.confirm` on purpose: it blocks, it is impossible to miss, and it is the one dialog
 * that cannot be dismissed by the click that opened it. A note field is offered for actions that
 * want a reason on record.
 */
export function confirmSpend(message: string): boolean {
  return window.confirm(message);
}
