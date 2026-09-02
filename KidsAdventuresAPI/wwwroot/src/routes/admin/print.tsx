import { createFileRoute, Link } from "@tanstack/react-router";
import { useState } from "react";

import { AdminPanel, AdminScreen, useAdminData } from "@/components/admin/AdminScreen";
import {
  BOOK_STATUS_TEXT,
  PRINT_STATUSES,
  PRINT_STATUS_TEXT,
  label,
} from "@/components/admin/labels";
import * as admin from "@/lib/api/admin";
import { BRAND_NAME } from "@/lib/brand";
import { buildPageMeta } from "@/lib/seo";

type PrintSearch = { status?: string };

export const Route = createFileRoute("/admin/print")({
  validateSearch: (search: Record<string, unknown>): PrintSearch => ({
    status:
      typeof search.status === "string" && PRINT_STATUSES.includes(search.status)
        ? search.status
        : undefined,
  }),
  head: () => {
    const { meta, links } = buildPageMeta({
      title: `ბეჭდვა და მიწოდება — ${BRAND_NAME} Admin`,
      description: "Print queue.",
      path: "/admin/print",
      noindex: true,
    });
    return { meta, links };
  },
  component: PrintPage,
});

/** What a parcel can move to from where it is. Backwards moves are deliberate and rare. */
const NEXT_STATUS: Record<string, string[]> = {
  AwaitingPrint: ["Printing", "Cancelled"],
  Printing: ["Shipped", "AwaitingPrint", "Cancelled"],
  Shipped: ["Delivered", "Printing"],
  Delivered: ["Shipped"],
  Cancelled: ["AwaitingPrint"],
};

/**
 * The parcels, by where they are.
 *
 * The API for this has existed since the first print order; nothing in the console called it,
 * so moving a parcel from "printing" to "shipped" — and sending the parent the tracking code —
 * was done in SQL or not at all.
 */
function PrintPage() {
  const { status } = Route.useSearch();
  const active = status ?? "AwaitingPrint";
  const { data, error, busy, refreshing, reload } = useAdminData(
    () => admin.listPrintQueue({ status: active, limit: 200 }),
    [active],
  );

  return (
    <AdminScreen
      active="print"
      title="ბეჭდვა და მიწოდება"
      subtitle="ამანათები სტატუსის მიხედვით; სტატუსის შეცვლა მშობელს წერილს უგზავნის"
    >
      <div className="panel orders-workspace">
        <div className="orders-toolbar print-tabs">
          {PRINT_STATUSES.map((s) => (
            <Link
              key={s}
              to="/admin/print"
              search={{ status: s }}
              className={`button ${active === s ? "" : "button-secondary"}`}
              aria-current={active === s ? "page" : undefined}
            >
              {PRINT_STATUS_TEXT[s]}
              {data?.counts?.[s] ? <em className="nav-count">{data.counts[s]}</em> : null}
            </Link>
          ))}
          {refreshing ? <span className="cell-subtitle">ახლდება…</span> : null}
        </div>

        <AdminPanel
          error={error}
          busy={busy}
          empty={data?.orders.length === 0}
          emptyText="ამ სტატუსში ამანათი არ არის."
        >
          {data && data.orders.length > 0 ? (
            <div className="table-scroll">
              <table className="data-table orders-table">
                <thead>
                  <tr>
                    <th>შემოვიდა</th>
                    <th>წიგნი</th>
                    <th>მიმღები</th>
                    <th>მისამართი</th>
                    <th>ფაილი</th>
                    <th>მოძრაობა</th>
                  </tr>
                </thead>
                <tbody>
                  {data.orders.map((row) => (
                    <PrintRow key={row.id} row={row} onChanged={reload} />
                  ))}
                </tbody>
              </table>
            </div>
          ) : null}
        </AdminPanel>
      </div>
    </AdminScreen>
  );
}

function PrintRow({ row, onChanged }: { row: admin.AdminPrintOrder; onChanged: () => void }) {
  const [tracking, setTracking] = useState(row.trackingCode ?? "");
  const [notify, setNotify] = useState(true);
  const [busy, setBusy] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);

  const move = async (next: string) => {
    if (next === "Shipped" && !tracking.trim()) {
      setNotice("გაგზავნისას თვალის მიდევნების კოდი აუცილებელია.");
      return;
    }
    if (next === "Cancelled" && !window.confirm("ამანათის გაუქმება? მშობელი წერილს მიიღებს."))
      return;
    setBusy(next);
    setNotice(null);
    try {
      const updated = await admin.updatePrintOrderStatus(row.id, {
        status: next,
        trackingCode: tracking.trim() || undefined,
        notifyCustomer: notify,
      });
      setNotice(`${updated.statusLabel}${notify ? " · მშობელს ეცნობა" : ""}`);
      onChanged();
    } catch (err) {
      setNotice(err instanceof Error ? err.message : "ვერ შესრულდა.");
    } finally {
      setBusy(null);
    }
  };

  const downloadPrintPdf = async () => {
    setBusy("pdf");
    setNotice(null);
    try {
      const { blob, filename } = await admin.downloadOrderPdf(row.orderId, "print");
      admin.saveBlob(blob, filename ?? `beki-${row.bookId}-print.pdf`);
    } catch (err) {
      setNotice(err instanceof Error ? err.message : "PDF ვერ ჩამოიტვირთა.");
    } finally {
      setBusy(null);
    }
  };

  return (
    <tr>
      <td>
        {admin.moment(row.createdAt)}
        <span className="cell-subtitle">{admin.ago(row.createdAt)} წინ</span>
      </td>
      <td className="book-cell">
        <Link to="/admin/orders" search={{ q: row.orderId }}>
          {row.bookTitle || "—"}
        </Link>
        <span className="cell-subtitle">
          {row.heroName ? `გმირი: ${row.heroName} · ` : ""}
          {label(BOOK_STATUS_TEXT, row.bookStatus)} · {row.totalFormatted}
        </span>
      </td>
      <td>
        {row.recipientName}
        <span className="cell-subtitle">{row.recipientPhone}</span>
        {row.customerEmail ? <span className="cell-subtitle">{row.customerEmail}</span> : null}
      </td>
      <td>
        {[row.city, row.addressLine1, row.addressLine2].filter(Boolean).join(", ")}
        {row.postalCode ? <span className="cell-subtitle">{row.postalCode}</span> : null}
        {row.notes ? <span className="cell-subtitle">შენიშვნა: {row.notes}</span> : null}
      </td>
      <td>
        {row.hasPrintPdf === false && !row.pdfIsReadingCopyFallback ? (
          <span className="attention-chip is-failed">საბეჭდი ფაილი არ არის</span>
        ) : (
          <button
            type="button"
            className="button button-secondary"
            disabled={busy !== null}
            onClick={() => void downloadPrintPdf()}
          >
            {busy === "pdf" ? "იტვირთება…" : "საბეჭდი PDF"}
          </button>
        )}
        {row.pdfIsReadingCopyFallback ? (
          <span className="cell-subtitle attention-chip is-review">
            საკითხავი ასლია — ბეჭდვისთვის მოუმზადებელი
          </span>
        ) : null}
      </td>
      <td>
        <div className="print-move">
          <input
            type="text"
            placeholder="თვალის მიდევნების კოდი"
            value={tracking}
            onChange={(e) => setTracking(e.target.value)}
            aria-label="თვალის მიდევნების კოდი"
          />
          <label className="print-notify">
            <input type="checkbox" checked={notify} onChange={(e) => setNotify(e.target.checked)} />
            მშობელს ეცნობოს
          </label>
          <div className="print-move-buttons">
            {(NEXT_STATUS[row.status] ?? []).map((next) => (
              <button
                key={next}
                type="button"
                className={`button ${next === "Cancelled" ? "button-secondary" : ""}`}
                disabled={busy !== null}
                onClick={() => void move(next)}
              >
                {busy === next ? "…" : `→ ${PRINT_STATUS_TEXT[next]}`}
              </button>
            ))}
          </div>
          {notice ? <span className="cell-subtitle">{notice}</span> : null}
        </div>
      </td>
    </tr>
  );
}
