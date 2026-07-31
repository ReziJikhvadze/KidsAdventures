"use client";

/* eslint-disable react-hooks/set-state-in-effect */

import { useEffect, useMemo, useState } from "react";
import { AdminShell } from "../components/AdminShell";
import {
  printJobStatusLabels,
  type PrintJob,
  type PrintJobStatus,
  useAdminState,
} from "../components/AdminState";
import {
  DateRangeFilter,
  inDateRange,
  type DateRange,
} from "../components/DateRangeFilter";
import { getPartnerOrderReference } from "../partner-data";

const partnerVisible: PrintJobStatus[] = [
  "Sent",
  "Accepted",
  "Printing",
  "Quality check",
  "Packed",
  "Ready for pickup",
];

const partnerNext: Partial<Record<PrintJobStatus, PrintJobStatus>> = {
  Sent: "Accepted",
  Accepted: "Printing",
  Printing: "Quality check",
  "Quality check": "Packed",
};

const partnerAction: Partial<Record<PrintJobStatus, string>> = {
  Sent: "Job-ის მიღება",
  Accepted: "ბეჭდვის დაწყება",
  Printing: "ხარისხის შემოწმებაზე გადაყვანა",
  "Quality check": "შეფუთულად მონიშვნა",
};

export default function PartnerPage() {
  const state = useAdminState();
  const [query, setQuery] = useState("");
  useEffect(() => {
    setQuery(new URLSearchParams(window.location.search).get("q") ?? "");
  }, []);
  const [statusFilter, setStatusFilter] = useState("all");
  const [dateRange, setDateRange] = useState<DateRange>({
    from: "2026-06-29",
    to: "2026-07-28",
    label: "30 დღე",
  });
  const visibleJobs = useMemo(
    () =>
      state.printJobs.filter(
        (job) => {
          const reference = getPartnerOrderReference(job.orderId);
          const matchesQuery = `${job.id} ${job.orderId} ${reference?.productionTitle ?? ""}`
            .toLowerCase()
            .includes(query.trim().toLowerCase());
          return (
            partnerVisible.includes(job.status) &&
            matchesQuery &&
            (statusFilter === "all" || job.status === statusFilter) &&
            inDateRange(job.createdDate, dateRange)
          );
        },
      ),
    [dateRange, query, state.printJobs, statusFilter],
  );
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const selected =
    visibleJobs.find((job) => job.id === selectedId) ?? visibleJobs[0] ?? null;

  return (
    <AdminShell
      active="partner"
      role="partner"
      title="Print Jobs"
      subtitle="BookLab-ისთვის გაგზავნილი, დადასტურებული საბეჭდი ვერსიები"
      actions={<span className="integration-pill success">BookLab · Connected</span>}
    >
      <DateRangeFilter label="Job-ის მიღების პერიოდი" onApply={setDateRange} />

      <section className="partner-inbox-alert">
        <span className="inbox-pulse" />
        <span>
          <strong>Partner Inbox — მთავარი სამუშაო queue</strong>
          <small>
            Admin-ის მიერ დადასტურებული Print Job აქ ავტომატურად ჩნდება.
            ელფოსტა მხოლოდ backup notification-ია.
          </small>
        </span>
        <span className="inbox-count">
          {state.printJobs.filter((job) => job.status === "Sent").length} ახალი
        </span>
      </section>

      <section className="partner-kpis">
        <article><span>ახალი</span><strong>{visibleJobs.filter((job) => job.status === "Sent").length}</strong><small>მიღებას ელოდება</small></article>
        <article><span>წარმოებაში</span><strong>{visibleJobs.filter((job) => ["Accepted", "Printing", "Quality check"].includes(job.status)).length}</strong><small>აქტიური სამუშაო</small></article>
        <article><span>შეფუთულია</span><strong>{visibleJobs.filter((job) => job.status === "Packed").length}</strong><small>კურიერის შექმნას ელოდება</small></article>
        <article><span>SLA</span><strong>96%</strong><small>ბოლო 30 დღე</small></article>
      </section>

      <section className="partner-layout">
        <article className="panel partner-jobs">
          <header className="panel-header">
            <div><p className="eyebrow">Production queue</p><h2>ჩემი Print Job-ები</h2></div>
            <div className="partner-queue-filters">
              <input
                aria-label="Print Job-ის ძებნა"
                onChange={(event) => setQuery(event.target.value)}
                placeholder="Job ან Order ID..."
                value={query}
              />
              <select
                aria-label="სტატუსი"
                onChange={(event) => setStatusFilter(event.target.value)}
                value={statusFilter}
              >
                <option value="all">ყველა სტატუსი</option>
                {partnerVisible.map((status) => (
                  <option key={status} value={status}>
                    {printJobStatusLabels[status]}
                  </option>
                ))}
              </select>
            </div>
          </header>
          <div className="partner-job-list">
            {visibleJobs.map((job) => {
              const reference = getPartnerOrderReference(job.orderId);
              return (
                <button
                  className={selected?.id === job.id ? "selected" : ""}
                  key={job.id}
                  onClick={() => setSelectedId(job.id)}
                  type="button"
                >
                  <span className={`book-thumb theme-${reference?.themeKey ?? "magic"}`}>{reference?.coverInitial ?? "A"}</span>
                  <span>
                    <strong>{reference?.productionTitle ?? job.orderId}</strong>
                    <small>{job.id} · {job.orderId}</small>
                  </span>
                  <span><strong>{printJobStatusLabels[job.status]}</strong><small>ვადა · {job.dueAt}</small></span>
                </button>
              );
            })}
            {!visibleJobs.length && <div className="table-empty">ახალი Print Job ჯერ არ გამოგზავნილა.</div>}
          </div>
        </article>

        {selected && <PartnerJobDetail job={selected} />}
      </section>

      <section className="partner-privacy-note">
        <strong>მონაცემთა მინიმიზაცია</strong>
        <p>
          ამ ხედში შეგნებულად არ ჩანს ბავშვის ფოტო, ინტერესები, Extra Wish,
          მშობლის ანგარიში ან გადახდის მონაცემები. პარტნიორს აქვს მხოლოდ
          წარმოებისა და pickup-ისთვის საჭირო ინფორმაცია. ეს გვერდი იყენებს
          ცალკე შეზღუდულ production DTO-ს და სრულ Orders მონაცემებს არ იტვირთავს.
        </p>
      </section>
    </AdminShell>
  );
}

function PartnerJobDetail({ job }: { job: PrintJob }) {
  const state = useAdminState();
  const reference = getPartnerOrderReference(job.orderId);
  const next = partnerNext[job.status];
  const [toast, setToast] = useState("");

  const showToast = (message: string) => {
    setToast(message);
    window.setTimeout(() => setToast(""), 2400);
  };

  const downloadApprovedManifest = () => {
    const lines = [
      "ADVENTRYA APPROVED PRINT SNAPSHOT",
      `Print Job: ${job.id}`,
      `Order: ${job.orderId}`,
      `Title: ${reference?.productionTitle ?? job.orderId}`,
      `File: ${job.approvedFileName ?? "Approved print PDF"}`,
      `File ID: ${job.approvedFileId ?? "—"}`,
      `Version: ${job.approvedVersion ?? job.version}`,
      `SHA-256: ${job.approvedHash ?? "—"}`,
      "Preflight: Passed 7/7",
      "Instruction: Print only this immutable approved snapshot.",
    ];
    const blob = new Blob([lines.join("\n")], { type: "text/plain;charset=utf-8" });
    const url = URL.createObjectURL(blob);
    const anchor = document.createElement("a");
    anchor.href = url;
    anchor.download = `${job.id}_approved-print-manifest.txt`;
    anchor.click();
    URL.revokeObjectURL(url);
    showToast("Approved snapshot-ის manifest ჩამოიტვირთა");
  };

  return (
    <article className="panel partner-detail">
      {toast && <div className="toast" role="status">{toast}</div>}
      <header className="partner-detail-head">
        <span className={`print-file-preview theme-${reference?.themeKey ?? "magic"}`}>
          <small>PRINT PDF</small>
          <strong>{reference?.productionTitle ?? job.orderId}</strong>
          <i>Cover + 7 pages</i>
        </span>
        <div>
          <p className="eyebrow">{job.id}</p>
          <h2>{reference?.productionTitle ?? job.orderId}</h2>
          <p>{job.orderId} · {printJobStatusLabels[job.status]}</p>
        </div>
      </header>

      <dl className="partner-job-facts">
        <div><dt>რაოდენობა</dt><dd>{reference?.quantity ?? 1} წიგნი</dd></div>
        <div><dt>ფორმატი</dt><dd>{reference?.trimSize ?? "210 × 210 mm"}</dd></div>
        <div><dt>გვერდები</dt><dd>{reference?.pageCount ?? "ყდა + 7"}</dd></div>
        <div><dt>ყდა</dt><dd>{reference?.binding ?? "Hardcover"}</dd></div>
        <div><dt>პროფილი</dt><dd>{reference?.colorProfile ?? "CMYK · 300 DPI"}</dd></div>
        <div><dt>ვადა</dt><dd>{job.dueAt}</dd></div>
        <div><dt>დამტკიცებული ვერსია</dt><dd>{job.approvedVersion ?? job.version}</dd></div>
        <div><dt>ფაილის hash</dt><dd>{job.approvedHash ?? "—"}</dd></div>
        <div><dt>Preflight</dt><dd>Passed · 7/7</dd></div>
        <div><dt>გაგზავნა</dt><dd>{job.sentAt ?? "—"}</dd></div>
        <div><dt>ბოლო განახლება</dt><dd>{job.lastUpdatedAt}</dd></div>
        <div><dt>პასუხისმგებელი</dt><dd>{job.owner}</dd></div>
        <div><dt>SLA</dt><dd>{job.sla}</dd></div>
      </dl>

      <section className="partner-file">
        <span>
          <strong>{job.approvedFileName ?? `Adventrya_${job.orderId}_print.pdf`}</strong>
          <small>Immutable approved snapshot · მხოლოდ ეს ფაილი იბეჭდება</small>
        </span>
        <button
          className="button button-secondary"
          onClick={downloadApprovedManifest}
          type="button"
        >
          Approved file-ის ჩამოტვირთვა
        </button>
      </section>

      <section className="partner-production">
        <h3>წარმოების სტატუსი</h3>
        <div className="partner-progress">
          {["Accepted", "Printing", "Quality check", "Packed"].map((status) => {
            const orderIndex = ["Sent", "Accepted", "Printing", "Quality check", "Packed", "Ready for pickup"].indexOf(job.status);
            const stepIndex = ["Sent", "Accepted", "Printing", "Quality check", "Packed", "Ready for pickup"].indexOf(status);
            return <span className={orderIndex >= stepIndex ? "done" : ""} key={status}>{printJobStatusLabels[status as PrintJobStatus]}</span>;
          })}
        </div>
        {next && (
          <button
            className="button button-primary full-button"
            onClick={() => state.updatePrintJob(job.orderId, next)}
            type="button"
          >
            {partnerAction[job.status]}
          </button>
        )}
        {job.status === "Packed" && (
          <div className="notice-card warning">
            წიგნი შეფუთულია. Adventrya Admin შექმნის საკურიერო შეკვეთას და აქ გამოჩნდება pickup label.
          </div>
        )}
        {job.courierCreated && (
          <div className="pickup-label">
            <span><strong>Pickup მზადაა</strong><small>Tracking · {job.trackingId}</small></span>
            <button
              className="button button-secondary"
              onClick={() => window.print()}
              type="button"
            >
              Label-ის ბეჭდვა
            </button>
          </div>
        )}
      </section>
    </article>
  );
}
