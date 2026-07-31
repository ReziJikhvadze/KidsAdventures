"use client";

import Link from "next/link";
import { useRouter } from "next/navigation";
import { useEffect, useRef, useState, type ReactNode } from "react";
import { navItems, orders } from "../data";
import { useAdminState } from "./AdminState";
import { Icon } from "./Icon";

type AdminShellProps = {
  active: string;
  title: string;
  subtitle?: string;
  actions?: ReactNode;
  children: ReactNode;
  role?: "admin" | "partner";
};

export function AdminShell({
  active,
  title,
  subtitle,
  actions,
  children,
  role = "admin",
}: AdminShellProps) {
  const [menuOpen, setMenuOpen] = useState(false);
  const [globalSearch, setGlobalSearch] = useState("");
  const searchRef = useRef<HTMLInputElement>(null);
  const router = useRouter();
  const adminState = useAdminState();
  const partnerMode = role === "partner";
  const navigationCounts: Record<string, number> = {
    production: orders.filter((order) =>
      ["Failed", "Review", "Generating"].includes(order.generationStatus),
    ).length,
    fulfillment: adminState.printJobs.filter(
      (job) => job.status !== "Ready for pickup",
    ).length,
    audit: adminState.auditEvents.filter(
      (event) => event.severity === "danger",
    ).length,
  };
  const adminGroups = [
    { label: "OPERATIONS", items: navItems.slice(0, 4) },
    { label: "MANAGEMENT", items: navItems.slice(4) },
  ];
  const partnerGroups = [
    {
      label: "PRODUCTION",
      items: [
        {
          key: "partner",
          label: "Print Jobs",
          href: "/partner",
          icon: "book",
        },
      ],
    },
  ];

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
    router.push(
      `${partnerMode ? "/partner" : "/orders"}?q=${encodeURIComponent(query)}`,
    );
  };

  return (
    <div className={`admin-app ${menuOpen ? "menu-open" : ""}`}>
      <aside className="sidebar">
        <div className="brand-row">
          <span className="brand-mark">A</span>
          <span>
            <strong>ADVENTRYA</strong>
            <small>{partnerMode ? "Print Partner" : "Admin"}</small>
          </span>
        </div>

        <nav aria-label="მთავარი ნავიგაცია" className="sidebar-nav">
          {(partnerMode ? partnerGroups : adminGroups).map((group) => (
            <div className="nav-group" key={group.label}>
              <span className="nav-group-label">{group.label}</span>
              {group.items.map((item) => {
                const count = partnerMode
                  ? adminState.printJobs.filter((job) => job.status === "Sent")
                      .length
                  : navigationCounts[item.key] ?? 0;
                return (
                  <Link
                    className={active === item.key ? "active" : ""}
                    href={item.href}
                    key={item.key}
                    onClick={() => setMenuOpen(false)}
                  >
                    <Icon name={item.icon} />
                    <span>{item.label}</span>
                    {count ? <span className="nav-count">{count}</span> : null}
                  </Link>
                );
              })}
            </div>
          ))}
        </nav>

        {!partnerMode && (
          <Link className="partner-preview-link" href="/partner">
            <span className="role-dot" />
            Print Partner-ის ხედვა
            <span aria-hidden="true">→</span>
          </Link>
        )}

        <div className="sidebar-user">
          <span className="avatar">{partnerMode ? "ნ" : "ო"}</span>
          <span>
            <strong>{partnerMode ? "ნიკა · BookLab" : "ომიკო"}</strong>
            <small>{partnerMode ? "Print Partner" : "Super Admin"}</small>
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
              placeholder={
                partnerMode
                  ? "Print Job ან Order ID..."
                  : "Order ID, მშობელი, ბავშვი ან წიგნი..."
              }
              ref={searchRef}
              type="search"
              value={globalSearch}
            />
            <kbd>⌘ K</kbd>
          </form>

          <div className="topbar-actions">
            <Link
              aria-label="კრიტიკული მოვლენები"
              className="topbar-icon"
              href={partnerMode ? "/partner" : "/audit?severity=danger"}
            >
              <Icon name="bell" />
              <span className="notification-dot">
                {partnerMode
                  ? adminState.printJobs.filter((job) => job.status === "Sent")
                      .length
                  : adminState.auditEvents.filter(
                      (event) => event.severity === "danger",
                    ).length}
              </span>
            </Link>
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
              {subtitle && <p>{subtitle}</p>}
            </div>
            {actions && <div className="page-actions">{actions}</div>}
          </header>
          {children}
        </main>
      </div>

      {menuOpen && (
        <button
          aria-label="ნავიგაციის დახურვა"
          className="sidebar-overlay"
          onClick={() => setMenuOpen(false)}
          type="button"
        />
      )}
    </div>
  );
}
