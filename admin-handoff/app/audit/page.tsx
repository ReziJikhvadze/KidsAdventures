"use client";

/* eslint-disable react-hooks/set-state-in-effect */

import Link from "next/link";
import { useEffect, useMemo, useState } from "react";
import { AdminShell } from "../components/AdminShell";
import { useAdminState } from "../components/AdminState";
import {
  DateRangeFilter,
  inDateRange,
  type DateRange,
} from "../components/DateRangeFilter";
import { Icon } from "../components/Icon";

export default function AuditPage() {
  const state = useAdminState();
  const [search, setSearch] = useState("");
  const [category, setCategory] = useState("all");
  const [actorRole, setActorRole] = useState("all");
  const [severity, setSeverity] = useState("all");
  const [dateRange, setDateRange] = useState<DateRange>({
    from: "2026-06-29",
    to: "2026-07-28",
    label: "30 დღე",
  });

  useEffect(() => {
    const params = new URLSearchParams(window.location.search);
    setSearch(params.get("q") ?? "");
    setSeverity(params.get("severity") ?? "all");
  }, []);

  const visibleEvents = useMemo(() => {
    const normalized = search.trim().toLowerCase();
    return state.auditEvents.filter((event) => {
      const matchesSearch =
        !normalized ||
        `${event.id} ${event.orderId ?? ""} ${event.action} ${event.detail} ${event.actor}`
          .toLowerCase()
          .includes(normalized);
      return (
        matchesSearch &&
        (category === "all" || event.category === category) &&
        (actorRole === "all" || event.actorRole === actorRole) &&
        (severity === "all" || event.severity === severity) &&
        inDateRange(event.eventDate, dateRange)
      );
    });
  }, [actorRole, category, dateRange, search, severity, state.auditEvents]);

  const exportCsv = () => {
    const escape = (value: string) => `"${value.replaceAll('"', '""')}"`;
    const rows = [
      ["Event ID", "Date", "Order", "Category", "Action", "Detail", "Actor", "Role", "Severity"],
      ...visibleEvents.map((event) => [
        event.id,
        `${event.eventDate} ${event.timestamp}`,
        event.orderId ?? "",
        event.category,
        event.action,
        event.detail,
        event.actor,
        event.actorRole,
        event.severity,
      ]),
    ];
    const blob = new Blob(
      [`\uFEFF${rows.map((row) => row.map(escape).join(",")).join("\n")}`],
      { type: "text/csv;charset=utf-8" },
    );
    const url = URL.createObjectURL(blob);
    const anchor = document.createElement("a");
    anchor.href = url;
    anchor.download = `adventrya-audit-${dateRange.from}-${dateRange.to}.csv`;
    anchor.click();
    URL.revokeObjectURL(url);
  };

  return (
    <AdminShell
      active="audit"
      title="Audit Log"
      subtitle="ყველა მნიშვნელოვანი მოქმედების უცვლელი ოპერაციული ისტორია"
      actions={
        <button
          className="button button-secondary"
          onClick={exportCsv}
          type="button"
        >
          CSV Export
        </button>
      }
    >
      <DateRangeFilter label="მოვლენის პერიოდი" onApply={setDateRange} />

      <section className="audit-integrity-banner">
        <span className="integrity-mark">✓</span>
        <span>
          <strong>Append-only Audit Trail</strong>
          <small>
            ჩანაწერი ვერ რედაქტირდება და ვერ იშლება. PDF approval, პარტნიორის
            სტატუსები, ფინანსური და საკურიერო მოქმედებები ინახება actor-ითა და დროით.
          </small>
        </span>
        <span className="integration-pill success">Integrity protected</span>
      </section>

      <section className="audit-summary">
        <article>
          <span>სულ მოვლენები</span>
          <strong>{state.auditEvents.length}</strong>
          <small>არჩეულ პროტოტიპში</small>
        </article>
        <article>
          <span>კრიტიკული</span>
          <strong>
            {state.auditEvents.filter((event) => event.severity === "danger").length}
          </strong>
          <small>საჭიროებს რეაგირებას</small>
        </article>
        <article>
          <span>PDF approvals</span>
          <strong>
            {state.auditEvents.filter((event) =>
              event.action.includes("PDF"),
            ).length}
          </strong>
          <small>ვერსია + hash დაფიქსირებულია</small>
        </article>
        <article>
          <span>Courier actions</span>
          <strong>
            {state.auditEvents.filter((event) => event.category === "DELIVERY").length}
          </strong>
          <small>idempotency key-ით</small>
        </article>
      </section>

      <section className="panel audit-workspace">
        <header className="panel-header audit-toolbar">
          <div className="table-search">
            <Icon name="search" size={17} />
            <input
              onChange={(event) => setSearch(event.target.value)}
              placeholder="Event ID, Order ID, მოქმედება ან actor..."
              type="search"
              value={search}
            />
          </div>
          <select
            aria-label="კატეგორია"
            onChange={(event) => setCategory(event.target.value)}
            value={category}
          >
            <option value="all">ყველა კატეგორია</option>
            <option>ORDER</option>
            <option>BOOK</option>
            <option>PRINT</option>
            <option>DELIVERY</option>
            <option>PAYMENT</option>
            <option>ACCESS</option>
          </select>
          <select
            aria-label="მოქმედი როლი"
            onChange={(event) => setActorRole(event.target.value)}
            value={actorRole}
          >
            <option value="all">ყველა როლი</option>
            <option>System</option>
            <option>Super Admin</option>
            <option>Operations</option>
            <option>Print Partner</option>
            <option>Courier</option>
          </select>
          <select
            aria-label="სიმძიმე"
            onChange={(event) => setSeverity(event.target.value)}
            value={severity}
          >
            <option value="all">ყველა შედეგი</option>
            <option value="danger">კრიტიკული</option>
            <option value="warning">რისკი</option>
            <option value="success">წარმატებული</option>
            <option value="info">ინფორმაცია</option>
          </select>
        </header>

        <div className="table-summary">
          <span>
            ნაჩვენებია <strong>{visibleEvents.length}</strong> მოვლენა
          </span>
          <span>ყველა დრო · GET</span>
        </div>

        <div className="table-scroll">
          <table className="data-table audit-table">
            <thead>
              <tr>
                <th>დრო / Event ID</th>
                <th>მოქმედება</th>
                <th>შეკვეთა</th>
                <th>Actor</th>
                <th>კატეგორია</th>
                <th>Integrity</th>
              </tr>
            </thead>
            <tbody>
              {visibleEvents.map((event) => (
                <tr key={event.id}>
                  <td>
                    <strong>{event.timestamp}</strong>
                    <span className="cell-subtitle">{event.id}</span>
                  </td>
                  <td>
                    <span className={`audit-action ${event.severity}`}>
                      <i />
                      <span>
                        <strong>{event.action}</strong>
                        <small>{event.detail}</small>
                      </span>
                    </span>
                  </td>
                  <td>
                    {event.orderId ? (
                      <Link className="order-link" href={`/orders/${event.orderId}?tab=activity`}>
                        {event.orderId}
                      </Link>
                    ) : (
                      "—"
                    )}
                  </td>
                  <td>
                    <strong>{event.actor}</strong>
                    <span className="cell-subtitle">{event.actorRole}</span>
                  </td>
                  <td>
                    <span className="audit-category">{event.category}</span>
                  </td>
                  <td>
                    <span className="immutable-chip">🔒 Immutable</span>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>

        {!visibleEvents.length && (
          <div className="table-empty">არჩეული ფილტრებით მოვლენა ვერ მოიძებნა.</div>
        )}
      </section>
    </AdminShell>
  );
}
