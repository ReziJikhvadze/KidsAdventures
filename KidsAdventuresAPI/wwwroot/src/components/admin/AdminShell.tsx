import { Link } from "@tanstack/react-router";
import { useEffect, useState, type ReactNode } from "react";

import { Icon } from "@/components/admin/Icon";
import * as admin from "@/lib/api/admin";

/**
 * The admin chrome: a sidebar, a heading, and the screen.
 *
 * The list is exactly what is built. Anything added later earns its place here by existing
 * first; a console that advertises screens it does not have teaches its one user to distrust
 * the ones it does.
 */

export type AdminNavKey =
  | "overview"
  | "orders"
  | "print"
  | "alarms"
  | "promo"
  | "admins"
  | "settings";

type NavItem = { key: AdminNavKey; label: string; href: string; icon: string };

const NAV_ITEMS: NavItem[] = [
  { key: "overview", label: "მიმოხილვა", href: "/admin", icon: "grid" },
  { key: "orders", label: "შეკვეთები", href: "/admin/orders", icon: "orders" },
  { key: "print", label: "ბეჭდვა და მიწოდება", href: "/admin/print", icon: "truck" },
  { key: "alarms", label: "შეტყობინებები", href: "/admin/alarms", icon: "bell" },
  { key: "promo", label: "პრომო კოდები", href: "/admin/promo", icon: "tag" },
  { key: "settings", label: "გამოშვების წესები", href: "/admin/settings", icon: "settings" },
  { key: "admins", label: "ადმინისტრატორები", href: "/admin/admins", icon: "users" },
];

/**
 * How many waived incidents nobody has looked at yet.
 *
 * In the chrome rather than on one screen because that is the point of it: the whole release
 * policy is built on shipping the book and telling somebody afterwards, and "afterwards" only
 * happens if the number is somewhere an operator passes anyway. A failed request leaves it at
 * zero and shows nothing — a badge that renders an error is worse than a badge that waits.
 */
function useOpenAlarmCount(): number {
  const [count, setCount] = useState(0);

  useEffect(() => {
    let cancelled = false;
    const read = () =>
      void admin
        .listAlarms({ open: true, limit: 1 })
        .then((result) => {
          if (!cancelled) setCount(result.openCount);
        })
        .catch(() => {
          /* the sidebar is not the place to report that a count could not be read */
        });

    read();
    const timer = window.setInterval(read, 60000);
    // Screens that close an alarm say so, and the badge follows without waiting a minute.
    window.addEventListener(ALARMS_CHANGED_EVENT, read);
    return () => {
      cancelled = true;
      window.clearInterval(timer);
      window.removeEventListener(ALARMS_CHANGED_EVENT, read);
    };
  }, []);

  return count;
}

/** Fired by any screen that reviews an alarm, so the badge does not lag a minute behind. */
export const ALARMS_CHANGED_EVENT = "beki-admin:alarms-changed";

export function announceAlarmsChanged(): void {
  window.dispatchEvent(new Event(ALARMS_CHANGED_EVENT));
}

export type AdminShellProps = {
  active: AdminNavKey;
  title: string;
  subtitle?: string;
  actions?: ReactNode;
  children: ReactNode;
  /** Signed-in operator, shown in the sidebar footer. */
  operatorName?: string;
};

export function AdminShell({
  active,
  title,
  subtitle,
  actions,
  children,
  operatorName,
}: AdminShellProps) {
  const [menuOpen, setMenuOpen] = useState(false);
  const [hangfireBusy, setHangfireBusy] = useState(false);
  const [hangfireError, setHangfireError] = useState<string | null>(null);
  const openAlarms = useOpenAlarmCount();
  const initial = (operatorName ?? "ო").trim().slice(0, 1) || "ო";

  const openHangfire = async () => {
    setHangfireBusy(true);
    setHangfireError(null);
    try {
      await admin.openHangfire();
    } catch (err) {
      setHangfireError(err instanceof Error ? err.message : "Hangfire ვერ გაიხსნა.");
    } finally {
      setHangfireBusy(false);
    }
  };

  return (
    <div className={`admin-app ${menuOpen ? "menu-open" : ""}`}>
      <aside className="sidebar">
        <div className="brand-row">
          <span className="brand-mark">B</span>
          <span>
            <strong>BEKI</strong>
            <small>Admin</small>
          </span>
        </div>

        <nav aria-label="მთავარი ნავიგაცია" className="sidebar-nav">
          <div className="nav-group">
            {NAV_ITEMS.map((item) => (
              <Link
                className={active === item.key ? "active" : ""}
                to={item.href}
                key={item.key}
                onClick={() => setMenuOpen(false)}
              >
                <Icon name={item.icon} />
                <span>{item.label}</span>
                {item.key === "alarms" && openAlarms > 0 ? (
                  <em className="nav-count">{openAlarms}</em>
                ) : null}
              </Link>
            ))}
          </div>
          <div className="nav-group">
            {/*
              The job dashboard lives on the API, not in this app, and cannot carry the session
              token in a plain link — so this asks the API for a short-lived cookie first.
            */}
            <button
              type="button"
              className="nav-link-button"
              disabled={hangfireBusy}
              onClick={() => void openHangfire()}
              title="Hangfire — ფონური სამუშაოების დაფა (ახალ ფანჯარაში)"
            >
              <Icon name="audit" />
              <span>{hangfireBusy ? "იხსნება…" : "ფონური სამუშაოები"}</span>
            </button>
            {hangfireError ? <small className="nav-error">{hangfireError}</small> : null}
          </div>
        </nav>

        <div className="sidebar-user">
          <span className="avatar">{initial}</span>
          <span>
            <strong>{operatorName ?? "ოპერატორი"}</strong>
            <small>ადმინისტრატორი</small>
          </span>
        </div>
      </aside>

      <div className="app-content">
        <header className="topbar">
          <button
            aria-expanded={menuOpen}
            aria-label="ნავიგაციის გახსნა"
            className="mobile-menu"
            onClick={() => setMenuOpen((open) => !open)}
            type="button"
          >
            <Icon name="menu" />
          </button>

          {/*
            Nothing when nothing is waiting. A badge that always shows a zero is a badge people
            stop reading, and this one has to still mean something on the day it says 14.
          */}
          {openAlarms > 0 ? (
            <Link
              className="admin-alarm-badge"
              to="/admin/alarms"
              aria-label={`${openAlarms} განუხილავი შეტყობინება`}
            >
              <Icon name="bell" size={16} />
              <span>{openAlarms}</span>
            </Link>
          ) : null}
        </header>

        <main className="main-content">
          <header className="page-heading">
            <div>
              <h1>{title}</h1>
              {subtitle ? <p>{subtitle}</p> : null}
            </div>
            {actions ? <div className="page-actions">{actions}</div> : null}
          </header>
          {children}
        </main>
      </div>

      {menuOpen ? (
        <button
          aria-label="ნავიგაციის დახურვა"
          className="sidebar-overlay"
          onClick={() => setMenuOpen(false)}
          type="button"
        />
      ) : null}
    </div>
  );
}
