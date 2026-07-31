"use client";

/* eslint-disable react-hooks/set-state-in-effect */

import Link from "next/link";
import { useEffect, useState } from "react";
import { AdminShell } from "../components/AdminShell";
import {
  type PrintJobStatus,
  useAdminState,
} from "../components/AdminState";
import {
  DateRangeFilter,
  inDateRange,
  type DateRange,
} from "../components/DateRangeFilter";
import { orders } from "../data";

const columns: PrintJobStatus[] = [
  "Ready for review",
  "Sent",
  "Accepted",
  "Printing",
  "Quality check",
  "Packed",
  "Ready for pickup",
];

const columnLabels: Record<PrintJobStatus, string> = {
  "Ready for review": "Admin Review",
  Sent: "გაგზავნილია",
  Accepted: "მიღებულია",
  Printing: "იბეჭდება",
  "Quality check": "ხარისხის შემოწმება",
  Packed: "შეფუთულია",
  "Ready for pickup": "კურიერს ელოდება",
};

export default function FulfillmentPage() {
  const state = useAdminState();
  const [toast, setToast] = useState("");
  const [view, setView] = useState<"pipeline" | "list">("pipeline");
  const [workflowFilter, setWorkflowFilter] = useState("all");
  useEffect(() => {
    const filter = new URLSearchParams(window.location.search).get("filter");
    if (filter === "ready") setWorkflowFilter("review");
    if (filter === "delayed") setWorkflowFilter("risk");
  }, []);
  const [dateRange, setDateRange] = useState<DateRange>({
    from: "2026-06-29",
    to: "2026-07-28",
    label: "30 დღე",
  });
  const visibleJobs = state.printJobs.filter((job) => {
    const matchesWorkflow =
      workflowFilter === "all" ||
      (workflowFilter === "review" && job.status === "Ready for review") ||
      (workflowFilter === "unaccepted" && job.status === "Sent") ||
      (workflowFilter === "risk" && job.sla !== "On track") ||
      (workflowFilter === "packed" && job.status === "Packed");
    return inDateRange(job.createdDate, dateRange) && matchesWorkflow;
  });

  const createCourier = (orderId: string) => {
    const result = state.createCourierOrder(orderId);
    const message =
      result === "creating"
        ? "საკურიერო შეკვეთა იქმნება — განმეორებითი მოთხოვნა დაბლოკილია"
        : result === "existing"
          ? "ამ Print Job-ზე საკურიერო შეკვეთა უკვე არსებობს"
          : result === "processing"
            ? "შეკვეთის შექმნა უკვე მიმდინარეობს"
            : "კურიერი მხოლოდ Packed სტატუსის შემდეგ იქმნება";
    setToast(message);
    window.setTimeout(() => setToast(""), 3000);
  };

  return (
    <AdminShell
      active="fulfillment"
      title="Print & Delivery"
      subtitle="Print Job-ები, პარტნიორის წარმოება, შეფუთვა და საკურიერო handoff"
      actions={
        <div className="segmented-control" aria-label="ხედის არჩევა">
          <button
            className={view === "pipeline" ? "active" : ""}
            onClick={() => setView("pipeline")}
            type="button"
          >
            Pipeline
          </button>
          <button
            className={view === "list" ? "active" : ""}
            onClick={() => setView("list")}
            type="button"
          >
            სია
          </button>
        </div>
      }
    >
      {toast && <div className="toast" role="status">{toast}</div>}
      <DateRangeFilter label="Print Job-ის შექმნის პერიოდი" onApply={setDateRange} />

      <section className="workflow-filter-bar" aria-label="ოპერაციული ფილტრები">
        {[
          ["all", "ყველა Job", state.printJobs.length],
          ["review", "PDF approval", state.printJobs.filter((job) => job.status === "Ready for review").length],
          ["unaccepted", "Partner ACK", state.printJobs.filter((job) => job.status === "Sent").length],
          ["risk", "SLA რისკი", state.printJobs.filter((job) => job.sla !== "On track").length],
          ["packed", "კურიერს ელოდება", state.printJobs.filter((job) => job.status === "Packed").length],
        ].map(([value, label, count]) => (
          <button
            className={workflowFilter === value ? "active" : ""}
            key={value}
            onClick={() => setWorkflowFilter(String(value))}
            type="button"
          >
            <span>{label}</span>
            <strong>{count}</strong>
          </button>
        ))}
      </section>

      <section className="handoff-control" aria-label="კონვეიერის კონტროლი">
        <article>
          <span className="handoff-step">1</span>
          <span><strong>Admin Review</strong><small>1 წიგნი ელოდება PDF approval-ს</small></span>
          <span className="integration-pill info">Adventrya</span>
        </article>
        <article>
          <span className="handoff-step">2</span>
          <span><strong>Partner Inbox</strong><small>Approved Job ავტომატურად ჩნდება პარტნიორთან</small></span>
          <span className="integration-pill success">Auto handoff</span>
        </article>
        <article>
          <span className="handoff-step">3</span>
          <span><strong>Production ACK</strong><small>პარტნიორმა Job 4 საათში უნდა მიიღოს</small></span>
          <span className="integration-pill active">SLA tracked</span>
        </article>
        <article>
          <span className="handoff-step">4</span>
          <span><strong>Packed → Courier</strong><small>Admin-ს ავტომატურად ეხსნება courier action</small></span>
          <span className="integration-pill scheduled">No skipped step</span>
        </article>
      </section>

      <section className="fulfillment-alert">
        <div>
          <strong>2 მიწოდება SLA-ს რისკშია</strong>
          <span>ADV-1042 ბოლო 34 საათია არ განახლებულა</span>
        </div>
        <Link href="/orders/ADV-1042?tab=fulfillment">შემოწმება →</Link>
      </section>

      {view === "pipeline" ? (
        <section className="kanban-board" aria-label="Print workflow">
          {columns.map((status) => {
            const jobs = visibleJobs.filter((job) => job.status === status);
            return (
              <article className="kanban-column" key={status}>
                <header>
                  <span>{columnLabels[status]}</span>
                  <strong>{jobs.length}</strong>
                </header>
                <div className="kanban-stack">
                  {jobs.length ? jobs.map((job) => {
                    const order = orders.find((item) => item.id === job.orderId);
                    return (
                      <section className="print-job-card" key={job.id}>
                        <div className="print-job-head">
                          <span className={`book-thumb theme-${order?.themeKey ?? "magic"}`}>
                            {order?.initial ?? "A"}
                          </span>
                          <span>
                            <strong>{order?.bookTitle ?? job.orderId}</strong>
                            <small>{job.id} · {job.orderId}</small>
                          </span>
                        </div>
                        <dl>
                          <div><dt>პარტნიორი</dt><dd>BookLab</dd></div>
                          <div><dt>Due</dt><dd>{job.dueAt}</dd></div>
                          <div><dt>Owner</dt><dd>{job.owner}</dd></div>
                          <div><dt>განახლება</dt><dd>{job.lastUpdatedAt}</dd></div>
                        </dl>
                        <span className={`job-sla ${job.sla.toLowerCase().replaceAll(" ", "-")}`}>
                          {job.sla}
                        </span>
                        {job.courierCreated && (
                          <p className="tracking-note">Courier · {job.trackingId}</p>
                        )}
                        <div className="job-actions">
                          <Link href={`/orders/${job.orderId}?tab=fulfillment`}>დეტალები</Link>
                          {status === "Packed" && !job.courierCreated && (
                            <button
                              disabled={job.courierStatus === "creating"}
                              onClick={() => createCourier(job.orderId)}
                              type="button"
                            >
                              {job.courierStatus === "creating"
                                ? "იქმნება…"
                                : "კურიერის შექმნა →"}
                            </button>
                          )}
                        </div>
                      </section>
                    );
                  }) : (
                    <div className="kanban-empty">ამ ეტაპზე job არ არის</div>
                  )}
                </div>
              </article>
            );
          })}
        </section>
      ) : (
        <section className="panel">
          <div className="table-scroll">
            <table className="data-table fulfillment-list-table">
              <thead>
                <tr>
                  <th>Print Job / Order</th>
                  <th>წიგნი</th>
                  <th>სტატუსი</th>
                  <th>Owner</th>
                  <th>Due / SLA</th>
                  <th>დამტკიცებული ფაილი</th>
                  <th>Courier</th>
                  <th />
                </tr>
              </thead>
              <tbody>
                {visibleJobs.map((job) => {
                  const order = orders.find((item) => item.id === job.orderId);
                  return (
                    <tr key={job.id}>
                      <td>
                        <strong>{job.id}</strong>
                        <span className="cell-subtitle">{job.orderId}</span>
                      </td>
                      <td>
                        <div className="book-cell">
                          <span className={`book-thumb theme-${order?.themeKey ?? "magic"}`}>
                            {order?.initial ?? "A"}
                          </span>
                          <span>
                            <strong>{order?.bookTitle ?? job.orderId}</strong>
                            <small>{order?.product}</small>
                          </span>
                        </div>
                      </td>
                      <td>
                        <span className="workflow-status">
                          {columnLabels[job.status]}
                        </span>
                      </td>
                      <td>{job.owner}</td>
                      <td>
                        <strong>{job.dueAt}</strong>
                        <span className={`job-sla ${job.sla.toLowerCase().replaceAll(" ", "-")}`}>
                          {job.sla}
                        </span>
                      </td>
                      <td>
                        {job.approvedVersion ?? "Approval pending"}
                        <span className="cell-subtitle">
                          {job.approvedHash ?? "ფაილი ჯერ არ ჩაკეტილა"}
                        </span>
                      </td>
                      <td>
                        {job.courierCreated
                          ? job.trackingId
                          : job.status === "Packed"
                            ? "შესაქმნელია"
                            : "ჯერ ადრეა"}
                      </td>
                      <td className="table-action">
                        <Link
                          className="icon-link"
                          href={`/orders/${job.orderId}?tab=fulfillment`}
                        >
                          →
                        </Link>
                      </td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          </div>
          {!visibleJobs.length && (
            <div className="table-empty">არჩეული ფილტრებით Print Job ვერ მოიძებნა.</div>
          )}
        </section>
      )}

      <section className="panel courier-operations">
        <header className="panel-header">
          <div>
            <p className="eyebrow">Courier adapter</p>
            <h2>საკურიერო შეკვეთები</h2>
          </div>
          <span className="integration-pill success">Mock adapter · Connected</span>
        </header>
        <div className="table-scroll">
          <table className="data-table">
            <thead>
              <tr>
                <th>Tracking</th>
                <th>შეკვეთა</th>
                <th>მიმღები</th>
                <th>მისამართი</th>
                <th>სტატუსი</th>
                <th>ბოლო განახლება</th>
                <th />
              </tr>
            </thead>
            <tbody>
              {visibleJobs.filter((job) => job.courierCreated).map((job) => {
                const order = orders.find((item) => item.id === job.orderId);
                return (
                  <tr key={job.id}>
                    <td>
                      <strong>{job.trackingId}</strong>
                      <span className="cell-subtitle">{job.externalCourierOrderId}</span>
                    </td>
                    <td><Link className="order-link" href={`/orders/${job.orderId}`}>{job.orderId}</Link></td>
                    <td>
                      <strong>{order?.parentName}</strong>
                      <span className="cell-subtitle">{order?.phone}</span>
                    </td>
                    <td>
                      {order?.city} · მისამართი დადასტურებულია
                      <span className="cell-subtitle">{order?.email}</span>
                    </td>
                    <td>
                      <span className="integration-pill info">შექმნილია</span>
                      <span className="cell-subtitle">{job.courierIdempotencyKey}</span>
                    </td>
                    <td>ახლახან</td>
                    <td className="table-action">
                      <Link
                        className="icon-link"
                        href={`/orders/${job.orderId}?tab=fulfillment`}
                      >
                        →
                      </Link>
                    </td>
                  </tr>
                );
              })}
              {!visibleJobs.some((job) => job.courierCreated) && (
                <tr><td colSpan={7}><div className="table-empty">Packed წიგნიდან ჯერ საკურიერო შეკვეთა არ შექმნილა.</div></td></tr>
              )}
            </tbody>
          </table>
        </div>
      </section>
    </AdminShell>
  );
}
