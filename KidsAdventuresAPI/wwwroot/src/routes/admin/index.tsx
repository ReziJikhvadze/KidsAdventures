import { createFileRoute, Link } from "@tanstack/react-router";

import { AdminPanel, AdminScreen, statusDot, useAdminData } from "@/components/admin/AdminScreen";
import { BOOK_STATUS_TEXT, ORDER_STATUS_TEXT, label } from "@/components/admin/labels";
import * as admin from "@/lib/api/admin";
import { BRAND_NAME } from "@/lib/brand";
import { buildPageMeta } from "@/lib/seo";

export const Route = createFileRoute("/admin/")({
  head: () => {
    const { meta, links } = buildPageMeta({
      title: `მიმოხილვა — ${BRAND_NAME} Admin`,
      description: "Overview.",
      path: "/admin",
      noindex: true,
    });
    return { meta, links };
  },
  component: OverviewPage,
});

/**
 * The numbers an operator opens the console to check, each one a door into the list behind it.
 *
 * This used to be a redirect to the orders list. A list answers "what happened to this family's
 * book"; it does not answer "is anybody waiting on me right now", which is the question the
 * console is opened with every morning.
 */
function OverviewPage() {
  const { data, error, busy, refreshing, reload } = useAdminData(() => admin.getOverview(), []);

  return (
    <AdminScreen
      active="overview"
      title="მიმოხილვა"
      subtitle="რა ხდება ახლა — და ვინ ელოდება ვინმეს აქედან"
      actions={
        <button
          type="button"
          className="button button-secondary"
          disabled={refreshing}
          onClick={reload}
        >
          {refreshing ? "ახლდება…" : "განახლება"}
        </button>
      }
    >
      <AdminPanel error={error} busy={busy}>
        {data ? (
          <>
            <div className="overview-grid">
              <Tile
                to="/admin/orders"
                search={{ flag: admin.NEEDS_ATTENTION }}
                tone={data.booksFailedCount + data.booksStuckCount > 0 ? "danger" : "muted"}
                value={data.booksFailedCount + data.booksStuckCount}
                title="საჭიროებს ყურადღებას"
                hint={`${data.booksFailedCount} ჩავარდნილი · ${data.booksStuckCount} გაჩერებული`}
              />
              <Tile
                to="/admin/orders"
                search={{ flag: admin.AWAITING_REVIEW }}
                tone={data.awaitingReviewCount > 0 ? "warning" : "muted"}
                value={data.awaitingReviewCount}
                title="ელოდება ვიზუალურ შემოწმებას"
                hint="დასრულებული წიგნები შეჩერებული ფაილით"
              />
              <Tile
                to="/admin/alarms"
                tone={data.openAlarmCount > 0 ? "warning" : "muted"}
                value={data.openAlarmCount}
                title="განუხილავი შეტყობინება"
                hint="რაც კონვეიერმა გაატარა და აღნიშნა"
              />
              <Tile
                to="/admin/orders"
                search={{ flag: admin.GENERATING }}
                tone="accent"
                value={data.booksGeneratingCount}
                title="ახლა იხატება"
                hint="წიგნები, რომლებზეც სამუშაო მიდის"
              />
              <Tile
                to="/admin/print"
                tone={data.printQueue.awaitingPrint > 0 ? "accent" : "muted"}
                value={data.printQueue.awaitingPrint}
                title="ბეჭდვის რიგში"
                hint={`${data.printQueue.printing} იბეჭდება · ${data.printQueue.shipped} გზაშია`}
              />
              <Tile
                to="/admin/orders"
                search={{ status: "Paid" }}
                tone="muted"
                value={data.paidTodayCount}
                title="დღეს გადახდილი (UTC)"
                hint={`${admin.gel(data.revenueTodayMinor)} დღეს · ${admin.gel(data.revenueMonthMinor)} ამ თვეში (${data.ordersMonthCount} შეკვეთა)`}
              />
            </div>

            <div className="panel orders-workspace">
              <div className="orders-toolbar">
                <strong>ბოლო შემთხვევები, რომლებიც ვინმეს ელოდება</strong>
                <span className="filter-count">{data.recentAttention.length}</span>
              </div>
              {data.recentAttention.length === 0 ? (
                <p className="empty-state">არავინ ელოდება — ყველაფერი გადის.</p>
              ) : (
                <div className="table-scroll">
                  <table className="data-table orders-table">
                    <thead>
                      <tr>
                        <th>თარიღი</th>
                        <th>მომხმარებელი</th>
                        <th>წიგნი</th>
                        <th>შეკვეთა</th>
                        <th>წიგნის მდგომარეობა</th>
                      </tr>
                    </thead>
                    <tbody>
                      {data.recentAttention.map((row) => (
                        <tr key={row.id}>
                          <td>
                            {admin.moment(row.createdAt)}
                            <span className="cell-subtitle">{row.id.slice(0, 8)}</span>
                          </td>
                          <td>
                            {row.customerEmail || row.customerPhone || "—"}
                            {row.heroName ? (
                              <span className="cell-subtitle">გმირი: {row.heroName}</span>
                            ) : null}
                          </td>
                          <td className="book-cell">
                            <Link to="/admin/orders" search={{ q: row.id }}>
                              {row.bookTitle || "—"}
                            </Link>
                            <span className="cell-subtitle">
                              {row.openAlarmCount > 0 ? `${row.openAlarmCount} შეტყობინება · ` : ""}
                              {row.withheld ? "შეჩერებული ფაილი · " : ""}
                              {row.isStale ? "გაჩერებული · " : ""}
                              {row.status === "Paid" && !row.fulfilledAt ? "შეუსრულებელი" : ""}
                            </span>
                          </td>
                          <td>
                            <span className={statusDot(row.status)} />{" "}
                            {label(ORDER_STATUS_TEXT, row.status)}
                          </td>
                          <td>
                            <span className={statusDot(row.bookStatus)} />{" "}
                            {label(BOOK_STATUS_TEXT, row.bookStatus)}
                            {row.progressMessage ? (
                              <span className="cell-subtitle">{row.progressMessage}</span>
                            ) : null}
                          </td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
              )}
            </div>
            <p className="cell-subtitle overview-stamp">
              დათვლილია {admin.moment(data.generatedAtUtc)} · დღე და თვე UTC-ით
            </p>
          </>
        ) : null}
      </AdminPanel>
    </AdminScreen>
  );
}

function Tile({
  to,
  search,
  tone,
  value,
  title,
  hint,
}: {
  to: "/admin/orders" | "/admin/alarms" | "/admin/print";
  search?: Record<string, string>;
  tone: "danger" | "warning" | "accent" | "muted";
  value: number;
  title: string;
  hint: string;
}) {
  return (
    <Link className={`overview-tile tone-${tone}`} to={to} search={search ?? {}}>
      <strong>{value}</strong>
      <span>{title}</span>
      <small>{hint}</small>
    </Link>
  );
}
