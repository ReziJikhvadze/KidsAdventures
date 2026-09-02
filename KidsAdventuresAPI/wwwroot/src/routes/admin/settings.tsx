import { createFileRoute, Link } from "@tanstack/react-router";
import { useState } from "react";

import { AdminPanel, AdminScreen, useAdminData } from "@/components/admin/AdminScreen";
import { CHECK_TEXT, CLASS_TEXT, checkLabel, severityText } from "@/components/admin/labels";
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
 * The release policy: which check stops a book and which merely leaves a note.
 *
 * The one switch that changes what an operator has to do every day sits above the board of
 * checks behind it. The notes themselves — the alarms — have their own screen now; they used to
 * be the third panel here, under twenty rows of policy, which is not where anybody looked.
 */
function SettingsPage() {
  const [notice, setNotice] = useState<string | null>(null);
  const [busyKey, setBusyKey] = useState<string | null>(null);

  const policy = useAdminData(() => admin.getReleasePolicy(), []);

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
      actions={
        <Link className="button button-secondary" to="/admin/alarms">
          შეტყობინებების ნახვა
        </Link>
      }
    >
      <div className="panel orders-workspace">
        <div className="policy-note">
          <p>
            „ბლოკერი“ აჩერებს ფაილს მშობლამდე; „ფლაგი“ წიგნს ატარებს და შეტყობინებას ტოვებს
            შეტყობინებების გვერდზე. ფლაგიდან ბლოკერზე გადასვლა უკვე გამოშვებულ ფაილს{" "}
            <strong>არ ითხოვს უკან</strong> — ის მხოლოდ შემდეგ წიგნებზე მოქმედებს. ბლოკერიდან ფლაგზე
            გადასვლა კი მაშინვე ამოწმებს შეჩერებულ წიგნებს და რაც იხსნება, გამოაქვს.
          </p>
          <p>
            ზოგიერთი შემოწმება აქ არ ჩანს: დაზიანებული ფაილი, ჰეშის ან გეომეტრიის შეუსაბამობა და
            მსგავსი პრობლემები ყოველთვის აჩერებს წიგნს — იქ გამოსაშვები არაფერია.
          </p>
        </div>

        {notice ? <p className="empty-state">{notice}</p> : null}

        <AdminPanel error={policy.error} busy={policy.busy}>
          <div className="policy-switch">
            <div>
              <h3>ვიზუალური შემოწმება ადამიანის მიერ</h3>
              <p>
                {policy.data?.humanReviewRequired
                  ? "ჩართულია: წიგნი მშობელს არ მიუვა, სანამ ოპერატორი დახატულ წიგნს არ დაადასტურებს შეკვეთის გვერდიდან."
                  : "გამორთულია: წიგნი მშობელს მიდის, ხოლო შემოწმება ჩანაწერში „გადადებულია წესით“ აღინიშნება და შეტყობინებებში გამოჩნდება."}
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
                        <span className="policy-check" title={text?.about ?? check.checkId}>
                          {checkLabel(check.checkId)}
                        </span>
                        <span className="cell-subtitle">{check.checkId}</span>
                        {text?.about ? <span className="alarm-detail">{text.about}</span> : null}
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
    </AdminScreen>
  );
}
