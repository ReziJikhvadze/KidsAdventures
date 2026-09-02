import { createFileRoute } from "@tanstack/react-router";
import { useState } from "react";

import { AdminPanel, AdminScreen, useAdminData } from "@/components/admin/AdminScreen";
import * as admin from "@/lib/api/admin";
import { BRAND_NAME } from "@/lib/brand";
import { buildPageMeta } from "@/lib/seo";

export const Route = createFileRoute("/admin/promo")({
  head: () => {
    const { meta, links } = buildPageMeta({
      title: `პრომო კოდები — ${BRAND_NAME} Admin`,
      description: "Promo codes.",
      path: "/admin/promo",
      noindex: true,
    });
    return { meta, links };
  },
  component: PromoPage,
});

/**
 * Promo codes, which until now could only be made in SQL.
 *
 * Create, see how often one was used, switch one off. Nothing here deletes: a code that was
 * redeemed is part of an order's price and stays on record.
 */
function PromoPage() {
  const { data, error, busy, reload } = useAdminData(() => admin.listPromoCodes(), []);
  const [code, setCode] = useState("");
  const [percent, setPercent] = useState("100");
  const [max, setMax] = useState("");
  const [oncePerUser, setOncePerUser] = useState(true);
  const [until, setUntil] = useState("");
  const [saving, setSaving] = useState(false);
  const [notice, setNotice] = useState<string | null>(null);
  const [busyId, setBusyId] = useState<string | null>(null);

  const create = async () => {
    const trimmed = code.trim().toUpperCase();
    if (!trimmed) {
      setNotice("ჩაწერე კოდი.");
      return;
    }
    const pct = Math.max(1, Math.min(100, Number(percent) || 0));
    setSaving(true);
    setNotice(null);
    try {
      await admin.createPromoCode({
        code: trimmed,
        discountPercent: pct === 100 ? undefined : pct,
        isFullDiscount: pct === 100,
        maxRedemptions: max ? Number(max) : undefined,
        oncePerUser,
        validUntilUtc: until ? new Date(until).toISOString() : undefined,
      });
      setCode("");
      setMax("");
      setUntil("");
      setNotice(`კოდი ${trimmed} შეიქმნა.`);
      reload();
    } catch (err) {
      setNotice(err instanceof Error ? err.message : "კოდი ვერ შეიქმნა.");
    } finally {
      setSaving(false);
    }
  };

  const toggle = async (row: admin.AdminPromoCode) => {
    setBusyId(row.id);
    setNotice(null);
    try {
      await admin.updatePromoCode(row.id, { isActive: !row.isActive });
      reload();
    } catch (err) {
      setNotice(err instanceof Error ? err.message : "ვერ შესრულდა.");
    } finally {
      setBusyId(null);
    }
  };

  return (
    <AdminScreen
      active="promo"
      title="პრომო კოდები"
      subtitle="ფასდაკლების კოდები და მათი გამოყენება"
    >
      <div className="panel orders-workspace">
        <div className="promo-form">
          <label className="field">
            <span>კოდი</span>
            <input
              value={code}
              onChange={(e) => setCode(e.target.value)}
              placeholder="მაგ. BEKI2026"
            />
          </label>
          <label className="field">
            <span>ფასდაკლება, %</span>
            <input
              type="number"
              min={1}
              max={100}
              value={percent}
              onChange={(e) => setPercent(e.target.value)}
            />
          </label>
          <label className="field">
            <span>მაქს. გამოყენება</span>
            <input
              type="number"
              min={1}
              value={max}
              onChange={(e) => setMax(e.target.value)}
              placeholder="უსასრულო"
            />
          </label>
          <label className="field">
            <span>ვადა (ბოლო დღე)</span>
            <input type="date" value={until} onChange={(e) => setUntil(e.target.value)} />
          </label>
          <label className="print-notify">
            <input
              type="checkbox"
              checked={oncePerUser}
              onChange={(e) => setOncePerUser(e.target.checked)}
            />
            თითო მომხმარებელზე ერთხელ
          </label>
          <button type="button" className="button" disabled={saving} onClick={() => void create()}>
            {saving ? "იქმნება…" : "კოდის შექმნა"}
          </button>
        </div>
        {notice ? <p className="empty-state">{notice}</p> : null}

        <AdminPanel
          error={error}
          busy={busy}
          empty={data?.length === 0}
          emptyText="კოდი ჯერ არ არის."
        >
          {data && data.length > 0 ? (
            <div className="table-scroll">
              <table className="data-table orders-table">
                <thead>
                  <tr>
                    <th>კოდი</th>
                    <th>ფასდაკლება</th>
                    <th>გამოყენება</th>
                    <th>ვადა</th>
                    <th>მდგომარეობა</th>
                    <th />
                  </tr>
                </thead>
                <tbody>
                  {data.map((row) => (
                    <tr key={row.id}>
                      <td>
                        <strong>{row.code}</strong>
                        <span className="cell-subtitle">
                          შეიქმნა {admin.moment(row.createdAtUtc)}
                        </span>
                      </td>
                      <td>
                        {row.isFullDiscount ? "100% (უფასო)" : `${row.discountPercent ?? 0}%`}
                      </td>
                      <td>
                        {row.redemptionCount}
                        {row.maxRedemptions ? ` / ${row.maxRedemptions}` : ""}
                        {row.oncePerUser ? (
                          <span className="cell-subtitle">თითო მომხმარებელზე ერთხელ</span>
                        ) : null}
                      </td>
                      <td>
                        {row.validUntilUtc ? admin.moment(row.validUntilUtc) : "უვადო"}
                        {row.validFromUtc ? (
                          <span className="cell-subtitle">
                            {admin.moment(row.validFromUtc)}-დან
                          </span>
                        ) : null}
                      </td>
                      <td>
                        <span className={row.isActive ? "dot dot-success" : "dot dot-danger"} />{" "}
                        {row.isActive ? "აქტიური" : "გამორთული"}
                      </td>
                      <td>
                        <button
                          type="button"
                          className="button button-secondary"
                          disabled={busyId !== null}
                          onClick={() => void toggle(row)}
                        >
                          {busyId === row.id ? "…" : row.isActive ? "გამორთვა" : "ჩართვა"}
                        </button>
                      </td>
                    </tr>
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
