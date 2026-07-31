"use client";

import { useState } from "react";
import { AdminShell } from "../components/AdminShell";
import {
  DateRangeFilter,
  type DateRange,
} from "../components/DateRangeFilter";

type Promotion = {
  code: string;
  value: string;
  appliesTo: string;
  uses: number;
  limit: number;
  status: "Active" | "Scheduled" | "Expired";
  end: string;
  startDate: string;
  endDate: string;
};

const initialPromotions: Promotion[] = [
  { code: "MAGIC20", value: "20%", appliesTo: "ყველა პროდუქტი", uses: 84, limit: 250, status: "Active", end: "31 აგვისტო", startDate: "2026-07-01", endDate: "2026-08-31" },
  { code: "GIFT100", value: "100%", appliesTo: "Digital", uses: 7, limit: 20, status: "Active", end: "15 აგვისტო", startDate: "2026-07-20", endDate: "2026-08-15" },
  { code: "PRINT10", value: "10 ₾", appliesTo: "Print Upgrade", uses: 31, limit: 100, status: "Active", end: "10 აგვისტო", startDate: "2026-06-15", endDate: "2026-08-10" },
  { code: "WELCOME14", value: "14 ₾", appliesTo: "პირველი Digital", uses: 0, limit: 500, status: "Scheduled", end: "1 სექტემბერი", startDate: "2026-08-01", endDate: "2026-09-01" },
  { code: "SUMMER", value: "15%", appliesTo: "Printed + Digital", uses: 126, limit: 126, status: "Expired", end: "20 ივლისი", startDate: "2026-06-01", endDate: "2026-07-20" },
];

export default function PromotionsPage() {
  const [promotions, setPromotions] = useState(initialPromotions);
  const [modalOpen, setModalOpen] = useState(false);
  const [draftCode, setDraftCode] = useState("");
  const [query, setQuery] = useState("");
  const [statusFilter, setStatusFilter] = useState("all");
  const [dateRange, setDateRange] = useState<DateRange>({
    from: "2026-06-29",
    to: "2026-07-28",
    label: "30 დღე",
  });
  const visiblePromotions = promotions.filter((promo) => {
    const matchesDate =
      promo.startDate <= dateRange.to && promo.endDate >= dateRange.from;
    const matchesQuery = `${promo.code} ${promo.appliesTo}`
      .toLowerCase()
      .includes(query.trim().toLowerCase());
    const matchesStatus =
      statusFilter === "all" || promo.status === statusFilter;
    return matchesDate && matchesQuery && matchesStatus;
  });

  return (
    <AdminShell
      active="promotions"
      title="Promotions"
      subtitle="Promocode-ების შექმნა, გამოყენების ლიმიტები და შედეგები"
      actions={
        <button className="button button-primary" onClick={() => setModalOpen(true)} type="button">
          + ახალი Promocode
        </button>
      }
    >
      <DateRangeFilter label="კამპანიის აქტივობის პერიოდი" onApply={setDateRange} />

      <section className="promotion-metrics">
        <article><span>აქტიური კოდები</span><strong>{promotions.filter((promo) => promo.status === "Active").length}</strong><small>{promotions.filter((promo) => promo.status === "Scheduled").length} დაგეგმილი კამპანია</small></article>
        <article><span>გამოყენება · არჩეული პერიოდი</span><strong>{visiblePromotions.reduce((sum, promo) => sum + promo.uses, 0)}</strong><small>filtered usage</small></article>
        <article><span>ფასდაკლება</span><strong>1,284 ₾</strong><small>შემოსავლის 9.7%</small></article>
        <article><span>Attributed revenue</span><strong>4,902 ₾</strong><small>Promocode orders</small></article>
      </section>

      <section className="panel">
        <header className="panel-header production-toolbar">
          <div><p className="eyebrow">Promotion control</p><h2>Promocode-ები</h2></div>
          <div className="toolbar-controls">
            <input
              aria-label="Promocode ძებნა"
              onChange={(event) => setQuery(event.target.value)}
              placeholder="კოდის ძებნა..."
              value={query}
            />
            <select
              aria-label="სტატუსი"
              onChange={(event) => setStatusFilter(event.target.value)}
              value={statusFilter}
            >
              <option value="all">ყველა სტატუსი</option>
              <option>Active</option>
              <option>Scheduled</option>
              <option>Expired</option>
            </select>
          </div>
        </header>
        <div className="table-scroll">
          <table className="data-table promo-table">
            <thead>
              <tr><th>კოდი</th><th>სარგებელი</th><th>გამოიყენება</th><th>გამოყენება</th><th>დასრულება</th><th>სტატუსი</th><th /></tr>
            </thead>
            <tbody>
              {visiblePromotions.map((promo) => (
                <tr key={promo.code}>
                  <td><span className="promo-code">{promo.code}</span></td>
                  <td><strong>{promo.value}</strong></td>
                  <td>{promo.appliesTo}</td>
                  <td>
                    <div className="usage-cell">
                      <span><i style={{ width: `${Math.min(100, (promo.uses / promo.limit) * 100)}%` }} /></span>
                      <small>{promo.uses} / {promo.limit}</small>
                    </div>
                  </td>
                  <td>{promo.end}</td>
                  <td><span className={`integration-pill ${promo.status.toLowerCase()}`}>{promo.status}</span></td>
                  <td className="table-action">
                    <button
                      className="text-button"
                      onClick={() =>
                        setPromotions((current) =>
                          current.map((item) =>
                            item.code === promo.code
                              ? {
                                  ...item,
                                  status:
                                    item.status === "Active"
                                      ? "Expired"
                                      : "Active",
                                }
                              : item,
                          ),
                        )
                      }
                      type="button"
                    >
                      {promo.status === "Active" ? "შეჩერება" : "გააქტიურება"}
                    </button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
        {!visiblePromotions.length && (
          <div className="table-empty">
            არჩეულ პერიოდში ან ფილტრებით Promocode ვერ მოიძებნა.
          </div>
        )}
      </section>

      {modalOpen && (
        <div className="modal-backdrop" role="presentation">
          <section className="modal promo-modal" aria-modal="true" role="dialog">
            <header>
              <div><p className="eyebrow">New promotion</p><h2>Promocode-ის შექმნა</h2></div>
              <button onClick={() => setModalOpen(false)} type="button">×</button>
            </header>
            <div className="promo-form">
              <label>კოდი<input autoFocus onChange={(event) => setDraftCode(event.target.value.toUpperCase())} placeholder="მაგ. MAGIC20" value={draftCode} /></label>
              <label>ფასდაკლების ტიპი<select defaultValue="percent"><option value="percent">პროცენტი</option><option>ფიქსირებული თანხა</option><option>100% უფასო</option></select></label>
              <label>მნიშვნელობა<input defaultValue="20" inputMode="numeric" /></label>
              <label>პროდუქტი<select defaultValue="all"><option value="all">ყველა პროდუქტი</option><option>Digital</option><option>Printed + Digital</option><option>Print Upgrade</option></select></label>
              <label>გამოყენების ლიმიტი<input defaultValue="100" inputMode="numeric" /></label>
              <label>დასრულების თარიღი<input defaultValue="2026-08-31" type="date" /></label>
            </div>
            <div className="notice-card warning">
              100%-იანი კოდი გადახდის ველებს გამორთავს, მაგრამ შეკვეთა იმავე Generation flow-ში გაგრძელდება.
            </div>
            <footer>
              <button className="button button-secondary" onClick={() => setModalOpen(false)} type="button">გაუქმება</button>
              <button
                className="button button-primary"
                disabled={!draftCode}
                onClick={() => {
                  setPromotions((current) => [
                    { code: draftCode, value: "20%", appliesTo: "ყველა პროდუქტი", uses: 0, limit: 100, status: "Active", end: "31 აგვისტო", startDate: "2026-07-28", endDate: "2026-08-31" },
                    ...current,
                  ]);
                  setModalOpen(false);
                  setDraftCode("");
                }}
                type="button"
              >
                შექმნა
              </button>
            </footer>
          </section>
        </div>
      )}
    </AdminShell>
  );
}
