import { createFileRoute, Link } from "@tanstack/react-router";
import { useEffect, useRef, useState } from "react";

import { AdminPanel, AdminScreen, useAdminData } from "@/components/admin/AdminScreen";
import { announceAlarmsChanged } from "@/components/admin/AdminShell";
import { RESOLUTION_TEXT, checkLabel, severityText } from "@/components/admin/labels";
import * as admin from "@/lib/api/admin";
import { BRAND_NAME } from "@/lib/brand";
import { buildPageMeta } from "@/lib/seo";

export const Route = createFileRoute("/admin/alarms")({
  head: () => {
    const { meta, links } = buildPageMeta({
      title: `შეტყობინებები — ${BRAND_NAME} Admin`,
      description: "Alarms.",
      path: "/admin/alarms",
      noindex: true,
    });
    return { meta, links };
  },
  component: AlarmsPage,
});

/**
 * The books that shipped with something waived, and the record of what was decided about them.
 *
 * Letting a book past a failed check is only defensible if somebody sees the failure afterwards,
 * so every waiver lands here with its evidence and stays until a person writes down what they
 * decided. The reviewed ones stay reachable: "has this happened before" is a question this list
 * has to be able to answer.
 */
function AlarmsPage() {
  const [showReviewed, setShowReviewed] = useState(false);
  const { data, error, busy, refreshing, reload } = useAdminData(
    () => admin.listAlarms({ open: !showReviewed, limit: 200 }),
    [showReviewed],
  );

  return (
    <AdminScreen
      active="alarms"
      title="შეტყობინებები"
      subtitle="რაც კონვეიერმა გაატარა და აღნიშნა — სანამ ვინმე არ ნახავს"
      actions={
        <button
          type="button"
          className={`button ${showReviewed ? "" : "button-secondary"}`}
          aria-pressed={showReviewed}
          onClick={() => setShowReviewed((v) => !v)}
        >
          {showReviewed ? "მხოლოდ განუხილავი" : "განხილულებიც"}
        </button>
      }
    >
      <div className="panel orders-workspace">
        <div className="orders-toolbar">
          <strong>{showReviewed ? "ბოლო შეტყობინებები" : "განუხილავი შეტყობინებები"}</strong>
          {data ? <span className="filter-count">{data.openCount}</span> : null}
          {refreshing ? <span className="cell-subtitle">ახლდება…</span> : null}
        </div>

        <AdminPanel
          error={error}
          busy={busy}
          empty={data?.items.length === 0}
          emptyText="განუხილავი შეტყობინება არ არის."
        >
          {data && data.items.length > 0 ? (
            <div className="table-scroll">
              <table className="data-table orders-table">
                <thead>
                  <tr>
                    <th>შემოწმება</th>
                    <th>წიგნი</th>
                    <th>მტკიცებულება</th>
                    <th>ბოლოს</th>
                    <th>დასკვნა</th>
                  </tr>
                </thead>
                <tbody>
                  {data.items.map((alarm) => (
                    <AlarmRow
                      key={alarm.id}
                      alarm={alarm}
                      onReviewed={() => {
                        announceAlarmsChanged();
                        reload();
                      }}
                    />
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

export function AlarmRow({
  alarm,
  onReviewed,
}: {
  alarm: admin.AdminAlarm;
  onReviewed: () => void;
}) {
  const [resolution, setResolution] = useState<admin.AlarmResolution>("acknowledged");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const review = async () => {
    setBusy(true);
    setError(null);
    try {
      await admin.reviewAlarm(alarm.id, resolution);
      onReviewed();
    } catch (err) {
      setError(err instanceof Error ? err.message : "ვერ შესრულდა.");
    } finally {
      setBusy(false);
    }
  };

  return (
    <tr>
      <td>
        <span className="policy-check">{checkLabel(alarm.checkId)}</span>
        <span className="cell-subtitle">
          {alarm.checkId} · {severityText(alarm.severity)}
        </span>
        <span className="alarm-detail">{alarm.detail}</span>
      </td>
      <td>
        {alarm.orderId ? (
          <Link to="/admin/orders" search={{ q: alarm.orderId }}>
            შეკვეთა {alarm.orderId.slice(0, 8)}
          </Link>
        ) : (
          "—"
        )}
        <span className="cell-subtitle">წიგნი {alarm.packId.slice(0, 8)}</span>
      </td>
      <td>
        <Evidence alarm={alarm} />
      </td>
      <td>
        {admin.moment(alarm.lastSeenUtc)}
        <span className="cell-subtitle">გაჩნდა {admin.moment(alarm.createdAtUtc)}</span>
      </td>
      <td>
        {alarm.reviewedAtUtc ? (
          <>
            {RESOLUTION_TEXT[alarm.resolution ?? ""] ?? alarm.resolution ?? "ნანახია"}
            <span className="cell-subtitle">
              {alarm.reviewedBy} · {admin.moment(alarm.reviewedAtUtc)}
            </span>
          </>
        ) : (
          <div className="alarm-review">
            <select
              aria-label="დასკვნა"
              value={resolution}
              onChange={(e) => setResolution(e.target.value as admin.AlarmResolution)}
            >
              {admin.ALARM_RESOLUTIONS.map((value) => (
                <option key={value} value={value}>
                  {RESOLUTION_TEXT[value]}
                </option>
              ))}
            </select>
            <button
              type="button"
              className="button button-secondary"
              disabled={busy}
              onClick={() => void review()}
            >
              {busy ? "…" : "განხილულია"}
            </button>
            {error ? <span className="cell-subtitle">{error}</span> : null}
          </div>
        )}
      </td>
    </tr>
  );
}

/**
 * The picture (or file) an alarm was raised about, in the row.
 *
 * It used to take the whole handback zip to see one evidence image. Images are fetched through
 * the authenticated route and shown inline; anything else becomes a download.
 */
function Evidence({ alarm }: { alarm: admin.AdminAlarm }) {
  const [url, setUrl] = useState<string | null>(null);
  const [open, setOpen] = useState(false);
  // The live object URL, for the unmount cleanup: a handle on a child's book is revoked once,
  // when the row leaves, whatever the render state was at the time.
  const live = useRef<string | null>(null);
  const isImage = /\.(png|jpe?g|webp)$/i.test(alarm.evidenceBlob ?? "");

  useEffect(() => {
    if (!open || !isImage || url) return;
    let cancelled = false;
    void admin.fetchImageObjectUrl(admin.alarmEvidencePath(alarm.id)).then((objectUrl) => {
      if (cancelled) {
        if (objectUrl) URL.revokeObjectURL(objectUrl);
        return;
      }
      live.current = objectUrl;
      setUrl(objectUrl);
    });
    return () => {
      cancelled = true;
    };
  }, [open, isImage, url, alarm.id]);

  useEffect(
    () => () => {
      if (live.current) URL.revokeObjectURL(live.current);
    },
    [],
  );

  if (!alarm.evidenceBlob) return <span className="cell-subtitle">—</span>;

  if (!isImage) {
    return (
      <a
        href={admin.alarmEvidencePath(alarm.id)}
        className="cell-subtitle"
        onClick={async (e) => {
          e.preventDefault();
          const objectUrl = await admin.fetchImageObjectUrl(admin.alarmEvidencePath(alarm.id));
          if (objectUrl) window.open(objectUrl, "_blank", "noopener");
        }}
      >
        ფაილის გახსნა
      </a>
    );
  }

  return (
    <div className="alarm-evidence">
      <button type="button" className="button button-secondary" onClick={() => setOpen((v) => !v)}>
        {open ? "დამალვა" : "სურათის ნახვა"}
      </button>
      {open ? (
        url ? (
          <a href={url} target="_blank" rel="noreferrer">
            <img src={url} alt={`მტკიცებულება — ${checkLabel(alarm.checkId)}`} />
          </a>
        ) : (
          <span className="cell-subtitle">იტვირთება…</span>
        )
      ) : null}
    </div>
  );
}
