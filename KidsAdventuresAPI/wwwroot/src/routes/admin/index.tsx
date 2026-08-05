import { createFileRoute } from "@tanstack/react-router";
import { useState } from "react";

import { AdminPanel, AdminScreen, useAdminData } from "@/components/admin/AdminScreen";
import * as admin from "@/lib/api/admin";
import { BRAND_NAME } from "@/lib/brand";
import { buildPageMeta } from "@/lib/seo";

export const Route = createFileRoute("/admin/")({
  head: () => {
    const { meta, links } = buildPageMeta({
      title: `Overview — ${BRAND_NAME} Admin`,
      description: "Operations overview.",
      path: "/admin",
      noindex: true,
    });
    return { meta, links };
  },
  component: OverviewPage,
});

function OverviewPage() {
  const [days, setDays] = useState(30);
  const { data, error, busy } = useAdminData(() => admin.getOverview(days), [days]);

  return (
    <AdminScreen
      active="overview"
      title="Operations Overview"
      subtitle="დღევანდელი მდგომარეობა და ყურადღების რიგი"
      actions={
        <select value={days} onChange={(e) => setDays(Number(e.target.value))} aria-label="პერიოდი">
          <option value={7}>ბოლო 7 დღე</option>
          <option value={30}>ბოლო 30 დღე</option>
          <option value={90}>ბოლო 90 დღე</option>
          <option value={365}>ბოლო წელი</option>
        </select>
      }
    >
      <AdminPanel error={error} busy={busy}>
        {data ? (
          <>
            <section className="kpi-grid">
              <Metric
                label="შემოსავალი"
                value={admin.gel(data.revenueMinorInWindow)}
                detail="გადახდილი და შესრულებული"
              />
              <Metric
                label="შეკვეთები"
                value={data.ordersInWindow}
                detail={`${data.paidOrdersInWindow} გადახდილი`}
              />
              <Metric
                label="ახალი მომხმარებლები"
                value={data.newCustomersInWindow}
                detail="რეგისტრაციები პერიოდში"
              />
              <Metric
                label="შექმნილი წიგნები"
                value={data.booksGeneratedInWindow}
                detail={`${data.booksInFlight} მუშავდება`}
              />
            </section>

            <section className="overview-grid">
              <article className="panel attention-panel">
                <div className="panel-head">
                  <h2>ყურადღების რიგი</h2>
                </div>
                <div className="event-list">
                  {/* Money taken with nothing delivered is the one row worth an alarm. */}
                  <AttentionRow
                    label="გადახდილი, შეუსრულებელი"
                    count={data.paidButUnfulfilled}
                    tone="danger"
                    detail="ფული ჩამოჭრილია, წიგნი არ მიწოდებულა"
                  />
                  <AttentionRow
                    label="ჩაფლავებული წიგნები"
                    count={data.booksFailed}
                    tone="danger"
                    detail="გენერაცია შეწყდა"
                  />
                  <AttentionRow
                    label="მუშავდება"
                    count={data.booksInFlight}
                    tone="warning"
                    detail="ჯერ არ დასრულებულა"
                  />
                  <AttentionRow
                    label="ბეჭდვის რიგი"
                    count={data.printOrdersAwaiting}
                    tone="warning"
                    detail="ელოდება დამუშავებას"
                  />
                </div>
              </article>
            </section>
          </>
        ) : null}
      </AdminPanel>
    </AdminScreen>
  );
}

function Metric({
  label,
  value,
  detail,
}: {
  label: string;
  value: string | number;
  detail: string;
}) {
  return (
    <article className="metric-card">
      <div className="metric-head">
        <span>{label}</span>
      </div>
      <strong>{value}</strong>
      <p>{detail}</p>
    </article>
  );
}

function AttentionRow({
  label,
  count,
  tone,
  detail,
}: {
  label: string;
  count: number;
  tone: "danger" | "warning";
  detail: string;
}) {
  return (
    <div className="event-row">
      <span className={count > 0 ? `dot dot-${tone}` : "dot dot-success"} />
      <span>
        <strong>{label}</strong>
        <span className="cell-subtitle">{detail}</span>
      </span>
      <span className="panel-count">{count}</span>
    </div>
  );
}
