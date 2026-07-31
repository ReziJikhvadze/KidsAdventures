"use client";

/* eslint-disable react-hooks/set-state-in-effect */

import Link from "next/link";
import { useEffect, useMemo, useState } from "react";
import { AdminShell } from "../components/AdminShell";
import {
  DateRangeFilter,
  inDateRange,
  type DateRange,
} from "../components/DateRangeFilter";
import { StatusBadge } from "../components/StatusBadge";
import { orders } from "../data";

const allProductionOrders = orders.filter((order) =>
  ["Failed", "Review", "Generating", "Ready"].includes(order.generationStatus),
);

const queueSummary = [
  { label: "Failed", count: 3, tone: "danger", note: "საჭიროებს მოქმედებას" },
  { label: "Review", count: 7, tone: "warning", note: "ყველაზე ძველი 1სთ 12წთ" },
  { label: "Generating", count: 4, tone: "info", note: "საშუალოდ 4წთ 18წმ" },
  { label: "Ready", count: 38, tone: "success", note: "დღეს დასრულებული" },
] as const;

export default function ProductionPage() {
  const [query, setQuery] = useState("");
  const [toast, setToast] = useState("");
  const [status, setStatus] = useState("all");
  useEffect(() => {
    const filter = new URLSearchParams(window.location.search).get("filter");
    if (filter === "failed") setStatus("Failed");
    if (filter === "review") setStatus("Review");
  }, []);
  const [language, setLanguage] = useState("all");
  const [dateRange, setDateRange] = useState<DateRange>({
    from: "2026-06-29",
    to: "2026-07-28",
    label: "30 დღე",
  });
  const productionOrders = useMemo(
    () =>
      allProductionOrders.filter((order) => {
        const matchesQuery = `${order.id} ${order.bookTitle}`
          .toLowerCase()
          .includes(query.toLowerCase());
        const matchesStatus =
          status === "all" || order.generationStatus === status;
        const matchesLanguage =
          language === "all" || order.bookLanguage === language;
        return (
          matchesQuery &&
          matchesStatus &&
          matchesLanguage &&
          inDateRange(order.createdDate, dateRange)
        );
      }),
    [dateRange, language, query, status],
  );

  return (
    <AdminShell
      active="production"
      title="Book Production"
      subtitle="გენერაციის queue, ხარისხის შემოწმება და კონკრეტული გვერდის კონტროლი"
      actions={
        <button
          className="button button-secondary"
          onClick={() => {
            setToast("Queue განახლებულია · ახალი ცვლილება არ არის");
            window.setTimeout(() => setToast(""), 2400);
          }}
          type="button"
        >
          Queue-ის განახლება
        </button>
      }
    >
      {toast && <div className="toast" role="status">{toast}</div>}
      <DateRangeFilter label="შეკვეთის პერიოდი" onApply={setDateRange} />

      <section className="production-summary">
        {queueSummary.map((item) => (
          <article className={`queue-stat ${item.tone}`} key={item.label}>
            <span>{item.label}</span>
            <strong>{item.count}</strong>
            <small>{item.note}</small>
          </article>
        ))}
      </section>

      <section className="panel">
        <header className="panel-header production-toolbar">
          <div>
            <p className="eyebrow">Live queue</p>
            <h2>წიგნების დამუშავება</h2>
          </div>
          <div className="toolbar-controls">
            <input
              aria-label="წიგნის ძებნა"
              onChange={(event) => setQuery(event.target.value)}
              placeholder="Order ID ან წიგნი..."
              value={query}
            />
            <select
              aria-label="გენერაციის სტატუსი"
              onChange={(event) => setStatus(event.target.value)}
              value={status}
            >
              <option value="all">ყველა სტატუსი</option>
              <option>Failed</option>
              <option>Review</option>
              <option>Generating</option>
              <option>Ready</option>
            </select>
            <select
              aria-label="ენა"
              onChange={(event) => setLanguage(event.target.value)}
              value={language}
            >
              <option value="all">ყველა ენა</option>
              <option>ქართული</option>
              <option>English</option>
            </select>
          </div>
        </header>

        <div className="table-scroll">
          <table className="data-table production-table">
            <thead>
              <tr>
                <th>შეკვეთა / წიგნი</th>
                <th>სტატუსი</th>
                <th>ეტაპი</th>
                <th>გვერდები</th>
                <th>დრო</th>
                <th>ღირებულება</th>
                <th>შემოწმება</th>
                <th />
              </tr>
            </thead>
            <tbody>
              {productionOrders.map((order, index) => (
                <tr key={order.id}>
                  <td>
                    <div className="book-cell">
                      <span className={`book-thumb theme-${order.themeKey}`}>
                        {order.initial}
                      </span>
                      <span>
                        <Link className="order-link" href={`/orders/${order.id}`}>
                          {order.bookTitle}
                        </Link>
                        <small>{order.id} · {order.bookLanguage}</small>
                      </span>
                    </div>
                  </td>
                  <td><StatusBadge value={order.generationStatus} /></td>
                  <td>
                    {order.generationStatus === "Failed"
                      ? "Illustration · Page 4"
                      : order.generationStatus === "Review"
                        ? "Admin quality review"
                        : "Book assembly complete"}
                  </td>
                  <td>{order.generationStatus === "Failed" ? "4 / 7" : "7 / 7"}</td>
                  <td>{index === 0 ? "8წთ 14წმ" : "4წთ 38წმ"}</td>
                  <td>{index === 0 ? "3.90 ₾" : "5.20 ₾"}</td>
                  <td>
                    <span className={order.generationStatus === "Failed" ? "review-score bad" : "review-score"}>
                      {order.generationStatus === "Failed" ? "Blocked" : "7/7 Passed"}
                    </span>
                  </td>
                  <td className="table-action">
                    <Link
                      className="button button-secondary compact-button"
                      href={`/orders/${order.id}?tab=generation`}
                    >
                      შემოწმება →
                    </Link>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
        {!productionOrders.length && (
          <div className="table-empty">
            არჩეულ პერიოდში შესაბამისი წიგნი არ მოიძებნა.
          </div>
        )}
      </section>

      <section className="production-insights">
        <article className="panel">
          <header className="panel-header">
            <div><p className="eyebrow">Cost watch</p><h2>დღევანდელი გენერაცია</h2></div>
          </header>
          <div className="insight-metrics">
            <div><span>52</span><small>დასრულებული წიგნი</small></div>
            <div><span>267.40 ₾</span><small>Estimated AI cost</small></div>
            <div><span>5.14 ₾</span><small>საშუალო / წიგნი</small></div>
          </div>
        </article>
        <article className="panel exception-panel">
          <header className="panel-header">
            <div><p className="eyebrow">Safety rule</p><h2>რეგენერაციის კონტროლი</h2></div>
          </header>
          <p>
            სრული წიგნის ხელახლა გენერაცია გამორთულია. ოპერატორს შეუძლია მხოლოდ
            კონკრეტული გვერდის რეგენერაცია, მიზეზისა და სავარაუდო ხარჯის დადასტურებით.
          </p>
        </article>
      </section>
    </AdminShell>
  );
}
