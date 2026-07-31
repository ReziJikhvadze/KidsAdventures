"use client";

import Link from "next/link";
import { useState } from "react";
import { AdminShell } from "./components/AdminShell";
import {
  DateRangeFilter,
  inDateRange,
  type DateRange,
} from "./components/DateRangeFilter";
import { StatusBadge } from "./components/StatusBadge";
import { attentionItems, orders } from "./data";

const queueLinks = [
  {
    label: "გენერაციის შეცდომა",
    count: 3,
    tone: "danger",
    href: "/production?filter=failed",
  },
  {
    label: "საჭიროებს წიგნის შემოწმებას",
    count: 7,
    tone: "warning",
    href: "/production?filter=review",
  },
  {
    label: "მზადაა დასაბეჭდად",
    count: 12,
    tone: "info",
    href: "/fulfillment?filter=ready",
  },
  {
    label: "მიწოდება დაგვიანებულია",
    count: 2,
    tone: "danger",
    href: "/fulfillment?filter=delayed",
  },
] as const;

export default function OverviewPage() {
  const [range, setRange] = useState<DateRange>({
    from: "2026-06-29",
    to: "2026-07-28",
    label: "30 დღე",
  });
  const visibleOrders = orders.filter((order) => inDateRange(order.createdDate, range));
  const kpis =
    range.label === "დღეს"
      ? [
          { label: "შეკვეთები", value: "14", detail: "დღეს", trend: "+8%" },
          { label: "შემოსავალი", value: "681 ₾", detail: "ფასდაკლებების შემდეგ", trend: "+11%" },
          { label: "Preview → Purchase", value: "23.1%", detail: "61 Preview-დან", trend: "+1.8%" },
          { label: "Digital → Print", value: "14.2%", detail: "65 ₾ upgrade", trend: "+0.9%" },
        ]
      : range.label === "7 დღე"
        ? [
            { label: "შეკვეთები", value: "46", detail: "ბოლო 7 დღე", trend: "+10%" },
            { label: "შემოსავალი", value: "2,284 ₾", detail: "ფასდაკლებების შემდეგ", trend: "+14%" },
            { label: "Preview → Purchase", value: "22.3%", detail: "206 Preview-დან", trend: "+2.0%" },
            { label: "Digital → Print", value: "13.9%", detail: "65 ₾ upgrade", trend: "+1.1%" },
          ]
        : [
            { label: "შეკვეთები", value: range.label === "30 დღე" ? "184" : String(visibleOrders.length), detail: range.label, trend: "+12%" },
            { label: "შემოსავალი", value: range.label === "30 დღე" ? "8,946 ₾" : "393 ₾", detail: "ფასდაკლებების შემდეგ", trend: "+18%" },
            { label: "Preview → Purchase", value: "21.4%", detail: "746 Preview-დან", trend: "+2.1%" },
            { label: "Digital → Print", value: "13.8%", detail: "65 ₾ upgrade", trend: "+1.4%" },
          ];

  return (
    <AdminShell
      active="overview"
      title="Overview"
      subtitle="დღევანდელი ოპერაციები და საკითხები, რომლებსაც მოქმედება სჭირდება"
      actions={
        <Link className="button button-secondary" href="/orders">
          ყველა შეკვეთა
        </Link>
      }
    >
      <DateRangeFilter onApply={setRange} />

      <section className="kpi-grid" aria-label="მთავარი მაჩვენებლები">
        {kpis.map((kpi) => (
          <article className="metric-card" key={kpi.label}>
            <div className="metric-head">
              <span>{kpi.label}</span>
              <span className="trend-positive">{kpi.trend}</span>
            </div>
            <strong>{kpi.value}</strong>
            <p>{kpi.detail}</p>
          </article>
        ))}
      </section>

      <section className="overview-grid">
        <article className="panel attention-panel">
          <header className="panel-header">
            <div>
              <p className="eyebrow">Action queue</p>
              <h2>საჭიროებს ყურადღებას</h2>
            </div>
            <span className="panel-count">24 საკითხი</span>
          </header>

          <div className="queue-list">
            {queueLinks.map((item) => (
              <Link className="queue-row" href={item.href} key={item.label}>
                <span className={`queue-indicator ${item.tone}`} />
                <span className="queue-label">{item.label}</span>
                <strong>{item.count}</strong>
                <span aria-hidden="true" className="row-arrow">
                  →
                </span>
              </Link>
            ))}
          </div>
        </article>

        <article className="panel sla-panel">
          <header className="panel-header">
            <div>
              <p className="eyebrow">Fulfillment health</p>
              <h2>ბეჭდვა და მიწოდება</h2>
            </div>
          </header>
          <div className="sla-chart" aria-label="SLA შესრულება 92%">
            <div className="sla-ring">
              <strong>92%</strong>
              <span>SLA-ში</span>
            </div>
            <div className="sla-details">
              <div>
                <span className="dot dot-success" />
                <p>ვადაში</p>
                <strong>46</strong>
              </div>
              <div>
                <span className="dot dot-warning" />
                <p>რისკში</p>
                <strong>4</strong>
              </div>
              <div>
                <span className="dot dot-danger" />
                <p>დაგვიანებული</p>
                <strong>2</strong>
              </div>
            </div>
          </div>
        </article>
      </section>

      <section className="panel">
        <header className="panel-header">
          <div>
            <p className="eyebrow">Live operations</p>
            <h2>ბოლო შეკვეთები</h2>
          </div>
          <Link className="text-link" href="/orders">
            სრული სია →
          </Link>
        </header>

        <div className="table-scroll">
          <table className="data-table compact-table">
            <thead>
              <tr>
                <th>შეკვეთა</th>
                <th>წიგნი</th>
                <th>ფორმატი</th>
                <th>გადახდა</th>
                <th>გენერაცია</th>
                <th>ბეჭდვა</th>
                <th>მიწოდება</th>
                <th />
              </tr>
            </thead>
            <tbody>
              {visibleOrders.slice(0, 5).map((order) => (
                <tr key={order.id}>
                  <td>
                    <Link className="order-link" href={`/orders/${order.id}`}>
                      {order.id}
                    </Link>
                    <span className="cell-subtitle">{order.createdAt}</span>
                  </td>
                  <td>
                    <div className="book-cell">
                      <span className={`book-thumb theme-${order.themeKey}`}>
                        {order.initial}
                      </span>
                      <span>
                        <strong>{order.bookTitle}</strong>
                        <small>{order.childName}</small>
                      </span>
                    </div>
                  </td>
                  <td>{order.product}</td>
                  <td>
                    <StatusBadge value={order.paymentStatus} />
                  </td>
                  <td>
                    <StatusBadge value={order.generationStatus} />
                  </td>
                  <td>
                    <StatusBadge value={order.printStatus} />
                  </td>
                  <td>
                    <StatusBadge value={order.deliveryStatus} />
                  </td>
                  <td className="table-action">
                    <a
                      aria-label={`${order.id} შეკვეთის გახსნა`}
                      className="icon-link"
                      href={`/orders/${order.id}`}
                    >
                      →
                    </a>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </section>

      <section className="panel">
        <header className="panel-header">
          <div>
            <p className="eyebrow">Exceptions</p>
            <h2>ბოლო პრობლემური მოვლენები</h2>
          </div>
        </header>
        <div className="event-list">
          {attentionItems.map((item) => (
            <Link href={item.href} className="event-row" key={item.id}>
              <span className={`event-icon ${item.tone}`}>!</span>
              <span>
                <strong>{item.title}</strong>
                <small>{item.description}</small>
              </span>
              <time>{item.time}</time>
            </Link>
          ))}
        </div>
      </section>
    </AdminShell>
  );
}
