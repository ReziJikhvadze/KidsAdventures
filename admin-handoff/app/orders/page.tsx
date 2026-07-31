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
import { Icon } from "../components/Icon";
import { StatusBadge } from "../components/StatusBadge";
import { getOrderOperationalView, orders } from "../data";

type QuickFilter = "all" | "attention" | "printed" | "digital" | "upgrade";

export default function OrdersPage() {
  const [search, setSearch] = useState("");
  useEffect(() => {
    setSearch(new URLSearchParams(window.location.search).get("q") ?? "");
  }, []);
  const [quickFilter, setQuickFilter] = useState<QuickFilter>("all");
  const [filtersOpen, setFiltersOpen] = useState(false);
  const [payment, setPayment] = useState("all");
  const [generation, setGeneration] = useState("all");
  const [print, setPrint] = useState("all");
  const [owner, setOwner] = useState("all");
  const [issue, setIssue] = useState("all");
  const [dateRange, setDateRange] = useState<DateRange>({
    from: "2026-06-29",
    to: "2026-07-28",
    label: "30 დღე",
  });

  const visibleOrders = useMemo(() => {
    const normalized = search.trim().toLowerCase();
    return orders.filter((order) => {
      const matchesSearch =
        !normalized ||
        [
          order.id,
          order.bookTitle,
          order.childName,
          order.parentName,
          order.email,
          order.phone,
        ]
          .join(" ")
          .toLowerCase()
          .includes(normalized);

      const matchesQuick =
        quickFilter === "all" ||
        (quickFilter === "attention" &&
          (order.generationStatus === "Failed" ||
            order.generationStatus === "Review" ||
            order.deliveryStatus === "Delayed")) ||
        (quickFilter === "printed" && order.product === "Printed + Digital") ||
        (quickFilter === "digital" && order.product === "Digital") ||
        (quickFilter === "upgrade" && order.product === "Print Upgrade");

      const matchesPayment =
        payment === "all" || order.paymentStatus.toLowerCase() === payment;
      const matchesGeneration =
        generation === "all" ||
        order.generationStatus.toLowerCase().replaceAll(" ", "-") === generation;
      const matchesPrint =
        print === "all" ||
        order.printStatus.toLowerCase().replaceAll(" ", "-") === print;
      const operational = getOrderOperationalView(order);
      const matchesOwner = owner === "all" || operational.owner === owner;
      const matchesIssue =
        issue === "all" ||
        (issue === "critical" && operational.issueTone === "danger") ||
        (issue === "risk" && operational.issueTone === "warning") ||
        (issue === "clear" && !operational.issue);
      const matchesDate = inDateRange(order.createdDate, dateRange);

      return (
        matchesSearch &&
        matchesQuick &&
        matchesPayment &&
        matchesGeneration &&
        matchesPrint &&
        matchesOwner &&
        matchesIssue &&
        matchesDate
      );
    });
  }, [dateRange, generation, issue, owner, payment, print, quickFilter, search]);

  const dateScopedOrders = orders.filter((order) =>
    inDateRange(order.createdDate, dateRange),
  );
  const quickCounts: Record<QuickFilter, number> = {
    all: dateScopedOrders.length,
    attention: dateScopedOrders.filter((order) => {
      const operational = getOrderOperationalView(order);
      return ["danger", "warning"].includes(operational.issueTone);
    }).length,
    printed: dateScopedOrders.filter(
      (order) => order.product === "Printed + Digital",
    ).length,
    digital: dateScopedOrders.filter((order) => order.product === "Digital")
      .length,
    upgrade: dateScopedOrders.filter((order) => order.product === "Print Upgrade")
      .length,
  };
  const visibleTotal = visibleOrders.reduce(
    (sum, order) =>
      sum + Number.parseFloat(order.price.replace(",", ".").replace(/[^\d.]/g, "")),
    0,
  );

  const resetFilters = () => {
    setSearch("");
    setQuickFilter("all");
    setPayment("all");
    setGeneration("all");
    setPrint("all");
    setOwner("all");
    setIssue("all");
    setDateRange({ from: "2026-06-29", to: "2026-07-28", label: "30 დღე" });
  };

  const exportCsv = () => {
    const escape = (value: string) => `"${value.replaceAll('"', '""')}"`;
    const rows = [
      [
        "Order",
        "Date",
        "Book",
        "Child",
        "Parent",
        "Email",
        "Phone",
        "Product",
        "Amount",
        "Payment",
        "Current stage",
        "Owner",
        "Issue",
        "SLA",
        "Next action",
      ],
      ...visibleOrders.map((order) => {
        const operational = getOrderOperationalView(order);
        return [
          order.id,
          order.createdDate,
          order.bookTitle,
          order.childName,
          order.parentName,
          order.email,
          order.phone,
          order.product,
          order.price,
          order.paymentStatus,
          operational.stage,
          operational.owner,
          operational.issue ?? "",
          operational.sla,
          operational.nextAction,
        ];
      }),
    ];
    const blob = new Blob(
      [`\uFEFF${rows.map((row) => row.map(escape).join(",")).join("\n")}`],
      { type: "text/csv;charset=utf-8" },
    );
    const url = URL.createObjectURL(blob);
    const anchor = document.createElement("a");
    anchor.href = url;
    anchor.download = `adventrya-orders-${dateRange.from}-${dateRange.to}.csv`;
    anchor.click();
    URL.revokeObjectURL(url);
  };

  return (
    <AdminShell
      active="orders"
      title="შეკვეთები"
      subtitle="Digital, Printed და Print Upgrade შეკვეთების ერთიანი მართვა"
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
      <DateRangeFilter onApply={setDateRange} />

      <section className="panel orders-workspace">
        <div className="orders-toolbar">
          <div className="table-search">
            <Icon name="search" size={17} />
            <input
              onChange={(event) => setSearch(event.target.value)}
              placeholder="Order ID, მშობელი, ბავშვი ან წიგნი..."
              type="search"
              value={search}
            />
          </div>
          <button
            aria-expanded={filtersOpen}
            className={`button button-secondary filter-toggle ${
              filtersOpen ? "active" : ""
            }`}
            onClick={() => setFiltersOpen((open) => !open)}
            type="button"
          >
            ფილტრები
            <span className="filter-count">
              {[payment, generation, print].filter((value) => value !== "all")
                .length || ""}
            </span>
          </button>
        </div>

        <div className="quick-filter-row" role="tablist" aria-label="სწრაფი ფილტრები">
          {[
            ["all", "ყველა", quickCounts.all],
            ["attention", "საჭიროებს ყურადღებას", quickCounts.attention],
            ["printed", "Printed", quickCounts.printed],
            ["digital", "Digital", quickCounts.digital],
            ["upgrade", "Print Upgrade", quickCounts.upgrade],
          ].map(([key, label, count]) => (
            <button
              aria-selected={quickFilter === key}
              className={quickFilter === key ? "active" : ""}
              key={key}
              onClick={() => setQuickFilter(key as QuickFilter)}
              role="tab"
              type="button"
            >
              {label}
              <span>{count}</span>
            </button>
          ))}
        </div>

        {filtersOpen && (
          <div className="filter-panel">
            <label>
              გადახდის სტატუსი
              <select value={payment} onChange={(e) => setPayment(e.target.value)}>
                <option value="all">ყველა</option>
                <option value="paid">გადახდილია</option>
                <option value="pending">მოლოდინში</option>
                <option value="failed">შეცდომა</option>
              </select>
            </label>
            <label>
              გენერაციის სტატუსი
              <select
                value={generation}
                onChange={(e) => setGeneration(e.target.value)}
              >
                <option value="all">ყველა</option>
                <option value="ready">მზადაა</option>
                <option value="review">შესამოწმებელია</option>
                <option value="failed">შეცდომა</option>
              </select>
            </label>
            <label>
              ბეჭდვის სტატუსი
              <select value={print} onChange={(e) => setPrint(e.target.value)}>
                <option value="all">ყველა</option>
                <option value="ready-for-print">მზადაა დასაბეჭდად</option>
                <option value="in-production">იბეჭდება</option>
                <option value="packed">შეფუთულია</option>
                <option value="shipped">გაგზავნილია</option>
              </select>
            </label>
            <label>
              მიმდინარე პასუხისმგებელი
              <select value={owner} onChange={(e) => setOwner(e.target.value)}>
                <option value="all">ყველა</option>
                <option value="Adventrya">Adventrya</option>
                <option value="BookLab">BookLab</option>
                <option value="Courier">Courier</option>
                <option value="Customer">Customer</option>
              </select>
            </label>
            <label>
              ოპერაციული მდგომარეობა
              <select value={issue} onChange={(e) => setIssue(e.target.value)}>
                <option value="all">ყველა</option>
                <option value="critical">კრიტიკული</option>
                <option value="risk">SLA რისკი</option>
                <option value="clear">პრობლემის გარეშე</option>
              </select>
            </label>
            <div className="filter-context">
              <span>არჩეული პერიოდი</span>
              <strong>{dateRange.from} — {dateRange.to}</strong>
            </div>
            <button className="text-button" onClick={resetFilters} type="button">
              ფილტრების გასუფთავება
            </button>
          </div>
        )}

        <div className="table-summary">
          <span>
            ნაჩვენებია <strong>{visibleOrders.length}</strong> შეკვეთა
          </span>
          <span>საერთო ღირებულება: {visibleTotal.toFixed(2)} ₾</span>
        </div>

        <div className="table-scroll">
          <table className="data-table orders-table">
            <thead>
              <tr>
                <th>შეკვეთა</th>
                <th>წიგნი / ბავშვი</th>
                <th>მომხმარებელი</th>
                <th>პროდუქტი / თანხა</th>
                <th>გადახდა</th>
                <th>მიმდინარე ეტაპი</th>
                <th>საკითხი / SLA</th>
                <th />
              </tr>
            </thead>
            <tbody>
              {visibleOrders.map((order) => {
                const operational = getOrderOperationalView(order);
                return (
                <tr
                  className={operational.issueTone === "danger" ? "critical-row" : ""}
                  key={order.id}
                >
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
                        <small>
                          {order.childName} · {order.bookLanguage}
                        </small>
                      </span>
                    </div>
                  </td>
                  <td>
                    <strong>{order.parentName}</strong>
                    <span className="cell-subtitle">{order.email}</span>
                    <span className="cell-subtitle">{order.phone}</span>
                  </td>
                  <td>
                    <span className="product-label">{order.product}</span>
                    <span className="cell-subtitle amount-line">{order.price}</span>
                  </td>
                  <td>
                    <StatusBadge value={order.paymentStatus} />
                  </td>
                  <td>
                    <span className="stage-cell">
                      <strong>{operational.stage}</strong>
                      <small>{operational.stageDetail}</small>
                      <em>Owner · {operational.owner}</em>
                    </span>
                  </td>
                  <td>
                    {operational.issue ? (
                      <span className={`issue-pill ${operational.issueTone}`}>
                        <strong>{operational.issue}</strong>
                        <small>SLA · {operational.sla}</small>
                      </span>
                    ) : (
                      <span className="issue-pill success">
                        <strong>პრობლემა არ არის</strong>
                        <small>{operational.sla}</small>
                      </span>
                    )}
                    <span className="cell-subtitle next-action">
                      შემდეგი · {operational.nextAction}
                    </span>
                  </td>
                  <td className="table-action">
                    <Link
                      aria-label={`${order.id} შეკვეთის გახსნა`}
                      className="icon-link"
                      href={`/orders/${order.id}`}
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

        {visibleOrders.length === 0 && (
          <div className="empty-state">
            <strong>შეკვეთა ვერ მოიძებნა</strong>
            <p>შეცვალეთ ძებნის ტექსტი ან გაასუფთავეთ ფილტრები.</p>
            <button className="button button-secondary" onClick={resetFilters}>
              გასუფთავება
            </button>
          </div>
        )}
      </section>
    </AdminShell>
  );
}
