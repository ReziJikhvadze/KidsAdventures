import { createFileRoute, Link } from "@tanstack/react-router";
import { useState } from "react";

import { AdminPanel, AdminScreen, useAdminData } from "@/components/admin/AdminScreen";
import * as admin from "@/lib/api/admin";
import { BRAND_NAME } from "@/lib/brand";
import { buildPageMeta } from "@/lib/seo";

export const Route = createFileRoute("/admin/settings")({
  head: () => {
    const { meta, links } = buildPageMeta({
      title: `გამოშვების წესები — ${BRAND_NAME} Admin`,
      description: "Release policy.",
      path: "/admin/settings",
      noindex: true,
    });
    return { meta, links };
  },
  component: SettingsPage,
});

/**
 * What each check actually looks at, in the operator's language.
 *
 * Kept in the client rather than sent from the API, like every other Georgian string in this
 * console. The API answers in check ids because the ids are the supplier's document and the
 * pipeline's own vocabulary; translating them is a presentation job, and a check this table has
 * no entry for still renders — under its raw id, which is exactly what an operator would need to
 * search for.
 */
const CHECK_TEXT: Record<string, { label: string; about: string }> = {
  human_review: {
    label: "ვიზუალური შემოწმება ადამიანის მიერ",
    about: "ოპერატორი ათვალიერებს დახატულ წიგნს და ხელს აწერს კონკრეტულ რენდერს.",
  },

  image_qa: {
    label: "სურათის ხარისხი",
    about: "ავტომატური შემოწმება პოულობს დამახინჯებულ ან თემას აცდენილ ილუსტრაციას.",
  },
  qa_unreadable: {
    label: "ხარისხის პასუხი ვერ წაიკითხა",
    about: "შემმოწმებელმა გაუგებარი პასუხი დააბრუნა. მტკიცებულება ინახება და შეტყობინებას ერთვის.",
  },
  centre_fold: {
    label: "შუა ნაკეცი",
    about: "გვერდის შუაში, ნაკეცის ხაზზე, მნიშვნელოვანი დეტალი ხომ არ ხვდება.",
  },
  cover_bands: {
    label: "ყდის ზოლები",
    about: "ყდის ზედა და ქვედა ზოლები — სათაური და ლოგო დაშვებულ არეშია თუ არა.",
  },
  VISUAL_QA: {
    label: "ვიზუალური QA",
    about: "ყველა გაშლილ გვერდს აქვს თუ არა ხარისხის ჩანაწერი, და რას ამბობს ის.",
  },
  COVER_CONTINUITY: {
    label: "ყდის უწყვეტობა",
    about: "ყდა ერთი მთლიანი ნახატია — წინა, ზურგი და კედელი ერთმანეთს ებმის.",
  },
  INTERIOR_CONTINUITY: {
    label: "შიდა გვერდების უწყვეტობა",
    about: "ყველა გაშლილ გვერდს აქვს კომპოზიციის ქვითარი და მიმოხილვა.",
  },
  TEXT_LAYER: {
    label: "ტექსტის ფენა",
    about: "ტექსტი ცალკე ფენაზეა და განლაგების ქვითარი ემთხვევა დაბეჭდილს.",
  },
  FONT_INTEGRITY: {
    label: "შრიფტების მთლიანობა",
    about: "შრიფტები PDF-შია ჩაშენებული და დამტკიცებული ნაკრებიდანაა.",
  },
  DIGITAL_GEOMETRY: {
    label: "ციფრული ვერსიის გეომეტრია",
    about: "საკითხავი ასლის ზომა და გვერდების რაოდენობა სპეციფიკაციას ემთხვევა.",
  },
  HANDBACK_COMPLETENESS: {
    label: "პაკეტის სისრულე",
    about: "მიმწოდებლისთვის გადასაცემ არქივში ყველა სავალდებულო ფაილია.",
  },

  PRESS_GEOMETRY: {
    label: "საბეჭდი გეომეტრია",
    about: "ბლიდი, ტრიმი და ნაკეცის ხაზები — ტიპოგრაფიის დაშვებებში.",
  },
  PRESS_COLOR: {
    label: "საბეჭდი ფერი (CMYK)",
    about: "ფერები CMYK/FOGRA39-შია და მთლიანი მელნის რაოდენობა ზღვარს არ სცილდება.",
  },
  PRESS_RESOLUTION: {
    label: "საბეჭდი გარჩევადობა",
    about: "სურათების რეალური გარჩევადობა ბეჭდვისთვის საკმარისია.",
  },
  TEXT_COLOR_INTEGRITY: {
    label: "ტექსტის ფერის მთლიანობა",
    about: "შავი ტექსტი ერთ საღებავზეა და ოთხფერიანად არ იბეჭდება.",
  },
  RENDER_VALIDATION: {
    label: "რენდერის ვალიდაცია",
    about: "დაგენერირებული ფაილი მართლა გაიხსნა და შემოწმდა, და არა მხოლოდ ჩაიწერა.",
  },
  QR: {
    label: "QR კოდი",
    about: "წიგნში ზუსტად ერთი QR არის და ის მუშა მისამართზე მიდის.",
  },

  ASSET_LOCK: {
    label: "აქტივების ჩაკეტვა",
    about: "წიგნი დამტკიცებულ შაბლონებსა და აქტივებზეა აწყობილი.",
  },
  EXACT_BEKI: {
    label: "ზუსტი BEKI",
    about: "ჰეშები და გეომეტრია ემთხვევა — ეს ინვარიანტია, გემოვნების საკითხი არა.",
  },
  SINGLE_COVER_MASTER: {
    label: "ერთი ყდის ორიგინალი",
    about: "ყდას ერთი წყარო აქვს და ის სწორი ზომისაა.",
  },
};

/** The four words an alarm can be closed with, in the operator's language. */
const RESOLUTION_TEXT: Record<admin.AlarmResolution, string> = {
  acknowledged: "ნანახია",
  fixed: "გამოსწორდა",
  wont_fix: "არ გამოსწორდება",
  false_alarm: "ცრუ განგაში",
};

const CLASS_TEXT: Record<string, string> = {
  all: "ყველა",
  press: "საბეჭდი",
  digital: "ციფრული",
  shared: "საერთო",
  package: "პაკეტი",
};

function checkLabel(checkId: string): string {
  return CHECK_TEXT[checkId]?.label ?? checkId;
}

/**
 * The release policy and what it has waived so far.
 *
 * Three panels, in the order somebody uses them: the one switch that changes what an operator has
 * to do every day, the board of checks behind it, and then the alarms — the books that shipped
 * with something waived, which is the whole reason the first two are allowed to be this permissive.
 */
function SettingsPage() {
  const [notice, setNotice] = useState<string | null>(null);
  const [busyKey, setBusyKey] = useState<string | null>(null);

  const policy = useAdminData(() => admin.getReleasePolicy(), []);
  const alarms = useAdminData(() => admin.listAlarms({ open: true, limit: 100 }), []);

  const setSeverity = async (setting: admin.AdminReleaseCheckSetting, severity: string) => {
    if (setting.severity === severity) return;
    const key = `${setting.checkId}:${setting.deliverableClass}`;
    setBusyKey(key);
    setNotice(null);
    try {
      const result = await admin.setReleasePolicy({
        checkId: setting.checkId,
        deliverableClass: setting.deliverableClass,
        severity,
      });
      // What the change DID, not that it was recorded. Loosening a check is a promise to the
      // parents whose finished books are sitting withheld under the old rule, and this number is
      // how many of those were re-examined because of this click.
      setNotice(
        result.publishedPacks > 0
          ? `${checkLabel(setting.checkId)}: ${severityText(severity)}. ${result.publishedPacks} შეჩერებული წიგნი გაიხსნა.`
          : `${checkLabel(setting.checkId)}: ${severityText(severity)}.`,
      );
      policy.reload();
      alarms.reload();
    } catch (err) {
      setNotice(err instanceof Error ? err.message : "წესი ვერ შეიცვალა.");
    } finally {
      setBusyKey(null);
    }
  };

  const humanReview = policy.data?.checks.find(
    (check) => check.checkId === "human_review" && check.deliverableClass === "all",
  );

  const checks = (policy.data?.checks ?? []).filter((check) => check.checkId !== "human_review");

  return (
    <AdminScreen
      active="settings"
      title="გამოშვების წესები"
      subtitle="რომელი შემოწმება აჩერებს წიგნს და რომელი მხოლოდ აფრთხილებს"
    >
      <div className="panel orders-workspace">
        {/*
          Said before anything can be clicked, because it is the one thing about this screen that
          is not reversible in the direction people assume. Tightening a rule protects the NEXT
          book; it cannot reach into the ones already sent.
        */}
        <div className="policy-note">
          <p>
            „ბლოკერი“ აჩერებს ფაილს მშობლამდე; „ფლაგი“ წიგნს ატარებს და შეტყობინებას ტოვებს აქ.
            ფლაგიდან ბლოკერზე გადასვლა უკვე გამოშვებულ ფაილს <strong>არ ითხოვს უკან</strong> — ის
            მხოლოდ შემდეგ წიგნებზე მოქმედებს. ბლოკერიდან ფლაგზე გადასვლა კი მაშინვე ამოწმებს
            შეჩერებულ წიგნებს და რაც იხსნება, გამოაქვს.
          </p>
          {/*
            Said once, so nobody spends an afternoon looking for a switch that does not exist. The
            checks that protect an invariant are not in this table at all, because a book that
            fails one of them has nothing to ship.
          */}
          <p>
            ზოგიერთი შემოწმება აქ არ ჩანს: დაზიანებული ფაილი, ჰეშის ან გეომეტრიის შეუსაბამობა და
            მსგავსი პრობლემები ყოველთვის აჩერებს წიგნს — იქ გამოსაშვები არაფერია.
          </p>
        </div>

        {notice ? <p className="empty-state">{notice}</p> : null}

        <AdminPanel error={policy.error} busy={policy.busy}>
          {/*
            The switch, out of the table on purpose. It is the setting that decides whether a
            person has to look at every book before a family gets it, and buried among twenty gate
            names nobody would find it — which is how a console ends up with a policy nobody knows
            is on.
          */}
          <div className="policy-switch">
            <div>
              <h3>ვიზუალური შემოწმება ადამიანის მიერ</h3>
              <p>
                {policy.data?.humanReviewRequired
                  ? "ჩართულია: წიგნი მშობელს არ მიუვა, სანამ ოპერატორი დახატულ წიგნს არ დაადასტურებს შეკვეთის გვერდიდან."
                  : "გამორთულია: წიგნი მშობელს მიდის, ხოლო შემოწმება ჩანაწერში „გადადებულია წესით“ აღინიშნება და აქ, შეტყობინებებში, გამოჩნდება."}
              </p>
            </div>
            <div className="policy-severity">
              <button
                type="button"
                aria-pressed={policy.data?.humanReviewRequired === true}
                disabled={busyKey !== null || !humanReview}
                onClick={() => humanReview && void setSeverity(humanReview, "blocker")}
              >
                ჩართული
              </button>
              <button
                type="button"
                aria-pressed={policy.data?.humanReviewRequired === false}
                disabled={busyKey !== null || !humanReview}
                onClick={() => humanReview && void setSeverity(humanReview, "flag")}
              >
                გამორთული
              </button>
            </div>
          </div>

          <div className="table-scroll">
            <table className="data-table orders-table">
              <thead>
                <tr>
                  <th>შემოწმება</th>
                  <th>ფაილი</th>
                  <th>სიმძიმე</th>
                  <th>ვინ შეცვალა</th>
                </tr>
              </thead>
              <tbody>
                {checks.map((check) => {
                  const key = `${check.checkId}:${check.deliverableClass}`;
                  const text = CHECK_TEXT[check.checkId];
                  return (
                    <tr key={key}>
                      <td>
                        {/* The tooltip is what the check looks at — the one thing a check id
                            cannot tell somebody who did not write the pipeline. */}
                        <span className="policy-check" title={text?.about ?? check.checkId}>
                          {checkLabel(check.checkId)}
                        </span>
                        <span className="cell-subtitle">{check.checkId}</span>
                      </td>
                      <td>{CLASS_TEXT[check.deliverableClass] ?? check.deliverableClass}</td>
                      <td>
                        <div className="policy-severity">
                          <button
                            type="button"
                            aria-pressed={check.severity === "blocker"}
                            disabled={busyKey !== null}
                            onClick={() => void setSeverity(check, "blocker")}
                          >
                            ბლოკერი
                          </button>
                          <button
                            type="button"
                            aria-pressed={check.severity === "flag"}
                            disabled={busyKey !== null}
                            onClick={() => void setSeverity(check, "flag")}
                          >
                            ფლაგი
                          </button>
                        </div>
                      </td>
                      <td>
                        {check.isDefault ? (
                          <span className="cell-subtitle">ნაგულისხმევი</span>
                        ) : (
                          <>
                            {check.updatedBy || "—"}
                            <span className="cell-subtitle">
                              {admin.moment(check.updatedAtUtc)}
                            </span>
                          </>
                        )}
                      </td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          </div>
        </AdminPanel>
      </div>

      <AlarmsPanel
        state={alarms}
        onReviewed={() => {
          alarms.reload();
        }}
      />
    </AdminScreen>
  );
}

function severityText(severity: string): string {
  return severity === "blocker" ? "ბლოკერი" : "ფლაგი";
}

/**
 * The books that shipped with something waived.
 *
 * This list is the other half of the owner's ruling. Letting a book past a failed check is only
 * defensible if somebody sees the failure afterwards, so every waiver lands here with its evidence
 * and stays until a person writes down what they decided about it.
 */
function AlarmsPanel({
  state,
  onReviewed,
}: {
  state: { data: admin.AdminAlarmList | null; error: string | null; busy: boolean };
  onReviewed: () => void;
}) {
  const [resolutions, setResolutions] = useState<Record<string, admin.AlarmResolution>>({});
  const [busyId, setBusyId] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  /*
    The evidence, through the download route that already exists.

    An alarm carries a storage key, and this console does not hand out storage keys — a link that
    outlives the request is a link that leaks a child's book. The handback package for the alarm's
    order is where that blob actually lives, so the button fetches that, as bytes, exactly the way
    the orders screen does.
  */
  const downloadEvidence = async (alarm: admin.AdminAlarm) => {
    if (!alarm.orderId) return;
    setError(null);
    try {
      const blob = await admin.downloadOrderPackage(alarm.orderId);
      const url = URL.createObjectURL(blob);
      const link = document.createElement("a");
      link.href = url;
      link.download = `BEKI_${alarm.packId}_HANDBACK_v002.zip`;
      link.click();
      URL.revokeObjectURL(url);
    } catch (err) {
      setError(err instanceof Error ? err.message : "პაკეტი ვერ ჩამოიტვირთა.");
    }
  };

  const review = async (alarm: admin.AdminAlarm) => {
    setBusyId(alarm.id);
    setError(null);
    try {
      await admin.reviewAlarm(alarm.id, resolutions[alarm.id] ?? "acknowledged");
      onReviewed();
    } catch (err) {
      setError(err instanceof Error ? err.message : "ვერ შესრულდა.");
    } finally {
      setBusyId(null);
    }
  };

  return (
    <div className="panel orders-workspace">
      <div className="orders-toolbar">
        <strong>განუხილავი შეტყობინებები</strong>
        {state.data ? <span className="filter-count">{state.data.openCount}</span> : null}
      </div>

      {error ? <p className="empty-state">{error}</p> : null}

      <AdminPanel error={state.error} busy={state.busy} empty={state.data?.items.length === 0}>
        {state.data && state.data.items.length > 0 ? (
          <div className="table-scroll">
            <table className="data-table orders-table">
              <thead>
                <tr>
                  <th>შემოწმება</th>
                  <th>წიგნი</th>
                  <th>ბოლოს</th>
                  <th>დახურვა</th>
                </tr>
              </thead>
              <tbody>
                {state.data.items.map((alarm) => (
                  <tr key={alarm.id}>
                    <td>
                      <span className="policy-check">{checkLabel(alarm.checkId)}</span>
                      <span className="cell-subtitle">
                        {alarm.checkId} · {severityText(alarm.severity)}
                      </span>
                      <span className="alarm-detail">{alarm.detail}</span>
                    </td>
                    <td>
                      {/*
                        The order is what the console can open; the pack id is shown beside it
                        because an alarm raised before an order was linked has only that.
                        Evidence is a storage key, and it travels in the handback zip on the
                        order's own page — this console does not hand out blob paths.
                      */}
                      {alarm.orderId ? (
                        <Link to="/admin/orders" search={{ q: alarm.orderId }}>
                          {alarm.orderId.slice(0, 8)}
                        </Link>
                      ) : (
                        "—"
                      )}
                      <span className="cell-subtitle">წიგნი {alarm.packId.slice(0, 8)}</span>
                      {alarm.evidenceBlob ? (
                        <span className="cell-subtitle" title={alarm.evidenceBlob}>
                          {alarm.orderId ? (
                            <button type="button" onClick={() => void downloadEvidence(alarm)}>
                              მტკიცებულება (ZIP)
                            </button>
                          ) : (
                            "მტკიცებულება პაკეტშია"
                          )}
                        </span>
                      ) : null}
                    </td>
                    <td>
                      {admin.moment(alarm.lastSeenUtc)}
                      <span className="cell-subtitle">
                        გაჩნდა {admin.moment(alarm.createdAtUtc)}
                      </span>
                    </td>
                    <td>
                      <div className="alarm-review">
                        {/*
                          Four words, not free text. They are the four the store's own constraint
                          accepts, and a box that let somebody type a fifth would either be
                          rewritten to "acknowledged" behind their back or refused by the database.
                        */}
                        <select
                          aria-label="დასკვნა"
                          value={resolutions[alarm.id] ?? "acknowledged"}
                          onChange={(e) =>
                            setResolutions((prev) => ({
                              ...prev,
                              [alarm.id]: e.target.value as admin.AlarmResolution,
                            }))
                          }
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
                          disabled={busyId !== null}
                          onClick={() => void review(alarm)}
                        >
                          {busyId === alarm.id ? "…" : "განხილულია"}
                        </button>
                      </div>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        ) : null}
      </AdminPanel>
    </div>
  );
}
