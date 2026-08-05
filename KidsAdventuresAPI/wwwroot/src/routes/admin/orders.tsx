import { createFileRoute } from "@tanstack/react-router";
import { useState } from "react";

import { AdminPanel, AdminScreen, statusDot, useAdminData } from "@/components/admin/AdminScreen";
import * as admin from "@/lib/api/admin";
import { BRAND_NAME } from "@/lib/brand";
import { buildPageMeta } from "@/lib/seo";

export const Route = createFileRoute("/admin/orders")({
  head: () => {
    const { meta, links } = buildPageMeta({
      title: `შეკვეთები — ${BRAND_NAME} Admin`,
      description: "Orders.",
      path: "/admin/orders",
      noindex: true,
    });
    return { meta, links };
  },
  component: OrdersPage,
});

const STATUSES = ["Pending", "Paid", "Fulfilled", "Failed", "Cancelled"];
const PAGE_SIZE = 25;

function OrdersPage() {
  // The shell's global search navigates here with ?q=, so seed the filter from it.
  const initialSearch =
    typeof window === "undefined"
      ? ""
      : (new URLSearchParams(window.location.search).get("q") ?? "");

  const [search, setSearch] = useState(initialSearch);
  const [status, setStatus] = useState("");
  const [page, setPage] = useState(1);

  const { data, error, busy } = useAdminData(
    () =>
      admin.listOrders({
        status: status || undefined,
        search: search || undefined,
        page,
        pageSize: PAGE_SIZE,
      }),
    [status, search, page],
  );

  const lastPage = Math.max(1, Math.ceil((data?.total ?? 0) / PAGE_SIZE));

  return (
    <AdminScreen active="orders" title="შეკვეთები" subtitle="ყველა მომხმარებლის შეკვეთა">
      <div className="panel orders-workspace">
        <div className="orders-toolbar">
          <input
            type="search"
            placeholder="ელფოსტა, ტელეფონი, წიგნი ან Order ID..."
            value={search}
            onChange={(e) => {
              setSearch(e.target.value);
              setPage(1);
            }}
            aria-label="ძებნა"
          />
          <select
            value={status}
            onChange={(e) => {
              setStatus(e.target.value);
              setPage(1);
            }}
            aria-label="სტატუსი"
          >
            <option value="">ყველა სტატუსი</option>
            {STATUSES.map((s) => (
              <option key={s} value={s}>
                {s}
              </option>
            ))}
          </select>
          {data ? <span className="filter-count">{data.total}</span> : null}
        </div>

        <AdminPanel error={error} busy={busy} empty={data?.items.length === 0}>
          {data && data.items.length > 0 ? (
            <>
              <table className="data-table orders-table">
                <thead>
                  <tr>
                    <th>თარიღი</th>
                    <th>მომხმარებელი</th>
                    <th>წიგნი</th>
                    <th>პაკეტი</th>
                    <th>სტატუსი</th>
                    <th>ჯამი</th>
                  </tr>
                </thead>
                <tbody>
                  {data.items.map((o) => (
                    <tr key={o.id}>
                      <td>
                        {new Date(o.createdAt).toLocaleDateString("ka-GE")}
                        <span className="cell-subtitle">{o.id.slice(0, 8)}</span>
                      </td>
                      <td>
                        {o.customerEmail || o.customerPhone || "—"}
                        {o.customerEmail && o.customerPhone ? (
                          <span className="cell-subtitle">{o.customerPhone}</span>
                        ) : null}
                      </td>
                      <td className="book-cell">{o.bookTitle || "—"}</td>
                      <td>{o.package}</td>
                      <td>
                        <span className={statusDot(o.status)} /> {o.status}
                        {o.failureReason ? (
                          <span className="cell-subtitle">{o.failureReason}</span>
                        ) : null}
                      </td>
                      <td>
                        {admin.gel(o.totalMinor)}
                        {o.discountMinor > 0 ? (
                          <span className="cell-subtitle amount-line">
                            −{admin.gel(o.discountMinor)}
                          </span>
                        ) : null}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>

              <div className="filter-context">
                <button
                  className="button button-secondary"
                  type="button"
                  disabled={page <= 1}
                  onClick={() => setPage(page - 1)}
                >
                  წინა
                </button>
                <span>
                  გვერდი {page} / {lastPage} · {data.total} ჩანაწერი
                </span>
                <button
                  className="button button-secondary"
                  type="button"
                  disabled={page >= lastPage}
                  onClick={() => setPage(page + 1)}
                >
                  შემდეგი
                </button>
              </div>
            </>
          ) : null}
        </AdminPanel>
      </div>
    </AdminScreen>
  );
}
