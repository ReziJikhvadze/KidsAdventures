import { Link } from "@tanstack/react-router";
import { useEffect, useState, type ReactNode } from "react";

import { Icon } from "@/components/admin/Icon";
import * as admin from "@/lib/api/admin";

/**
 * The admin chrome: a sidebar, a heading, and the screen.
 *
 * It began as a port of a design handoff with eight navigation items, six of which were
 * rendered greyed out because no route existed behind them. A console that advertises six
 * screens it does not have teaches its one user to distrust the two it does, so the list is
 * now exactly what is built. Anything added later earns its place here by existing first.
 *
 * The global search box went with them: it did one thing — jump to the orders list with a
 * query — and the orders list has its own search field on it.
 */

export type AdminNavKey = "orders" | "admins" | "settings";

type NavItem = { key: AdminNavKey; label: string; href: string; icon: string };

const NAV_ITEMS: NavItem[] = [
  { key: "orders", label: "შეკვეთები", href: "/admin/orders", icon: "orders" },
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
    return () => {
      cancelled = true;
      window.clearInterval(timer);
    };
  }, []);

  return count;
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
  const openAlarms = useOpenAlarmCount();
  const initial = (operatorName ?? "ო").trim().slice(0, 1) || "ო";

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
              </Link>
            ))}
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
              to="/admin/settings"
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
