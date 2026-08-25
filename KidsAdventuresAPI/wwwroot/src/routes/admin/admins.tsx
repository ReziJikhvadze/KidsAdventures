import { createFileRoute } from "@tanstack/react-router";
import { useState } from "react";

import { AdminPanel, AdminScreen, useAdminData } from "@/components/admin/AdminScreen";
import * as admin from "@/lib/api/admin";
import { BRAND_NAME } from "@/lib/brand";
import { buildPageMeta } from "@/lib/seo";

export const Route = createFileRoute("/admin/admins")({
  head: () => {
    const { meta, links } = buildPageMeta({
      title: `ადმინისტრატორები — ${BRAND_NAME} Admin`,
      description: "Administrators.",
      path: "/admin/admins",
      noindex: true,
    });
    return { meta, links };
  },
  component: AdminsPage,
});

const PAGE_SIZE = 25;

/**
 * Who can open this console, granted from the same list as everything else about a customer —
 * because the person being promoted is a customer. There is no separate staff directory to
 * keep in step, and inventing one would mean two places to look for the same person.
 */
function AdminsPage() {
  const [search, setSearch] = useState("");
  const [page, setPage] = useState(1);
  const [busyId, setBusyId] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);

  const { data, error, busy, reload } = useAdminData(
    () => admin.listCustomers({ search: search || undefined, page, pageSize: PAGE_SIZE }),
    [search, page],
  );

  const lastPage = Math.max(1, Math.ceil((data?.total ?? 0) / PAGE_SIZE));

  const toggle = async (row: admin.AdminCustomerRow) => {
    setBusyId(row.id);
    setNotice(null);
    try {
      const result = await admin.setUserAdmin(row.id, !row.isAdmin);
      setNotice(result.note);
      reload();
    } catch (err) {
      // The API refuses three things — demoting yourself, a configured super-admin, or the last
      // admin — and each refusal carries the reason. Showing it verbatim is the whole point.
      setNotice(err instanceof Error ? err.message : "ვერ შესრულდა.");
    } finally {
      setBusyId(null);
    }
  };

  return (
    <AdminScreen active="admins" title="ადმინისტრატორები" subtitle="ვის აქვს ამ კონსოლზე წვდომა">
      <div className="panel orders-workspace">
        <div className="orders-toolbar">
          <input
            type="search"
            placeholder="ელფოსტა, ტელეფონი ან სახელი..."
            value={search}
            onChange={(e) => {
              setSearch(e.target.value);
              setPage(1);
            }}
            aria-label="ძებნა"
          />
          {data ? <span className="filter-count">{data.total}</span> : null}
        </div>

        <p className="cell-subtitle">
          როლი ტოკენში იწერება შესვლისას — ახლად დანიშნულმა ადმინმა სისტემიდან უნდა გამოვიდეს და
          ხელახლა შევიდეს, რომ ცვლილება ამოქმედდეს.
        </p>
        {notice ? <p className="empty-state">{notice}</p> : null}

        <AdminPanel error={error} busy={busy} empty={data?.items.length === 0}>
          {data && data.items.length > 0 ? (
            <>
              <table className="data-table orders-table">
                <thead>
                  <tr>
                    <th>მომხმარებელი</th>
                    <th>ტელეფონი</th>
                    <th>წიგნები</th>
                    <th>ჯამური შენაძენი</th>
                    <th>როლი</th>
                    <th />
                  </tr>
                </thead>
                <tbody>
                  {data.items.map((row) => (
                    <tr key={row.id}>
                      <td>
                        {row.displayName || row.email || "—"}
                        {row.displayName && row.email ? (
                          <span className="cell-subtitle">{row.email}</span>
                        ) : null}
                      </td>
                      <td>{row.phoneNumber || "—"}</td>
                      <td>
                        {row.bookCount}
                        <span className="cell-subtitle">{row.orderCount} შეკვეთა</span>
                      </td>
                      <td>{admin.gel(row.spendMinor)}</td>
                      <td>
                        <span className={row.isAdmin ? "dot dot-success" : "dot dot-warning"} />{" "}
                        {row.isAdmin ? "ადმინისტრატორი" : "მომხმარებელი"}
                      </td>
                      <td>
                        <button
                          type="button"
                          className={`button ${row.isAdmin ? "button-secondary" : ""}`}
                          disabled={busyId !== null}
                          onClick={() => void toggle(row)}
                        >
                          {busyId === row.id
                            ? "…"
                            : row.isAdmin
                              ? "უფლების მოხსნა"
                              : "ადმინად დანიშვნა"}
                        </button>
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
