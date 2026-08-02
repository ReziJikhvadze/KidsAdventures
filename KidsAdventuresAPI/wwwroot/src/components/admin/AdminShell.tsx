import { Link } from "@tanstack/react-router";
import { useEffect, useRef, useState, type ReactNode } from "react";

import { Icon } from "@/components/admin/Icon";

/**
 * The approved admin chrome, rebuilt for TanStack Start.
 *
 * The markup and every class name are the handoff's (app/components/AdminShell.tsx) so
 * the ported `admin-console.css` styles it unchanged. What differs is only the framework
 * seam: `next/link` becomes TanStack `Link`, and `next/navigation` becomes `useNavigate`.
 *
 * Badge counts were read from mocked React state in the prototype; here they are passed
 * in by whichever screen already loaded the real numbers, so the shell stays free of data
 * fetching of its own.
 */

export type AdminNavKey =
  | "overview"
  | "orders"
  | "production"
  | "fulfillment"
  | "customers"
  | "promotions"
  | "audit"
  | "settings";

type NavItem = {
  key: AdminNavKey;
  label: string;
  href: string;
  icon: string;
  /**
   * False while the screen has no route yet. The item still appears, because the
   * approved navigation is the approved navigation — but it does not link anywhere,
   * so the console never advertises a page that 404s.
   */
  built: boolean;
};

// Copied from the handoff's data.ts so the labels and order match the approved design.
const NAV_ITEMS: NavItem[] = [
  { key: "overview", label: "Overview", href: "/admin", icon: "grid", built: true },
  { key: "orders", label: "შეკვეთები", href: "/admin/orders", icon: "orders", built: true },
  { key: "production", label: "Book Production", href: "/admin/production", icon: "book", built: false },
  { key: "fulfillment", label: "Print & Delivery", href: "/admin/fulfillment", icon: "truck", built: false },
  { key: "customers", label: "მომხმარებლები", href: "/admin/customers", icon: "users", built: false },
  { key: "promotions", label: "Promotions", href: "/admin/promotions", icon: "tag", built: false },
  { key: "audit", label: "Audit Log", href: "/admin/audit", icon: "audit", built: false },
  { key: "settings", label: "Settings", href: "/admin/settings", icon: "settings", built: false },
];

const GROUPS = [
  { label: "OPERATIONS", items: NAV_ITEMS.slice(0, 4) },
  { label: "MANAGEMENT", items: NAV_ITEMS.slice(4) },
];

export type AdminShellProps = {
  active: AdminNavKey;
  title: string;
  subtitle?: string;
  actions?: ReactNode;
  children: ReactNode;
  /** Live badge counts, keyed by nav key. Absent or zero renders no badge. */
  counts?: Partial<Record<AdminNavKey, number>>;
  /** Signed-in operator, shown in the sidebar footer. */
  operatorName?: string;
};

export function AdminShell({
  active,
  title,
  subtitle,
  actions,
  children,
  counts = {},
  operatorName,
}: AdminShellProps) {
  const [menuOpen, setMenuOpen] = useState(false);
  const [globalSearch, setGlobalSearch] = useState("");
  const searchRef = useRef<HTMLInputElement>(null);

  useEffect(() => {
    const handleShortcut = (event: KeyboardEvent) => {
      if ((event.metaKey || event.ctrlKey) && event.key.toLowerCase() === "k") {
        event.preventDefault();
        searchRef.current?.focus();
      }
    };
    window.addEventListener("keydown", handleShortcut);
    return () => window.removeEventListener("keydown", handleShortcut);
  }, []);

  const submitSearch = () => {
    const query = globalSearch.trim();
    if (!query) return;
    // Full assign rather than a router push: the orders screen reads its filter from
    // the query string on mount, so this is the one place a hard navigation is simpler.
    window.location.assign(`/admin/orders?q=${encodeURIComponent(query)}`);
  };

  const initial = (operatorName ?? "ო").trim().slice(0, 1) || "ო";

  return (
    <div className={`admin-app ${menuOpen ? "menu-open" : ""}`}>
      <aside className="sidebar">
        <div className="brand-row">
          <span className="brand-mark">A</span>
          <span>
            <strong>ADVENTRYA</strong>
            <small>Admin</small>
          </span>
        </div>

        <nav aria-label="მთავარი ნავიგაცია" className="sidebar-nav">
          {GROUPS.map((group) => (
            <div className="nav-group" key={group.label}>
              <span className="nav-group-label">{group.label}</span>
              {group.items.map((item) => {
                const count = counts[item.key] ?? 0;
                const body = (
                  <>
                    <Icon name={item.icon} />
                    <span>{item.label}</span>
                    {item.built && count ? <span className="nav-count">{count}</span> : null}
                  </>
                );

                if (!item.built) {
                  return (
                    <span
                      key={item.key}
                      aria-disabled="true"
                      title="ჯერ არ არის ხელმისაწვდომი"
                      style={{ opacity: 0.45, cursor: "default" }}
                    >
                      {body}
                    </span>
                  );
                }

                return (
                  <Link
                    className={active === item.key ? "active" : ""}
                    to={item.href}
                    key={item.key}
                    onClick={() => setMenuOpen(false)}
                  >
                    {body}
                  </Link>
                );
              })}
            </div>
          ))}
        </nav>

        <div className="sidebar-user">
          <span className="avatar">{initial}</span>
          <span>
            <strong>{operatorName ?? "ოპერატორი"}</strong>
            <small>Super Admin</small>
          </span>
          <span aria-hidden="true">•••</span>
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

          <form
            className="global-search"
            onSubmit={(event) => {
              event.preventDefault();
              submitSearch();
            }}
          >
            <Icon name="search" size={18} />
            <input
              aria-label="გლობალური ძებნა"
              onChange={(event) => setGlobalSearch(event.target.value)}
              placeholder="Order ID, მშობელი, ბავშვი ან წიგნი..."
              ref={searchRef}
              type="search"
              value={globalSearch}
            />
            <kbd>⌘ K</kbd>
          </form>

          <div className="topbar-actions">
            <div className="language-switch" aria-label="ადმინის ენა">
              <strong>ქარ</strong>
              <span>ENG</span>
            </div>
          </div>
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
