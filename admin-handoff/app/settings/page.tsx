"use client";

import Link from "next/link";
import { useState } from "react";
import { AdminShell } from "../components/AdminShell";
import { useAdminState } from "../components/AdminState";

const roles = [
  {
    name: "Super Admin",
    people: "2 წევრი",
    access: "ყველა მოდული, ფინანსური მოქმედებები, როლები და ინტეგრაციები",
  },
  {
    name: "Operations / Support",
    people: "4 წევრი",
    access: "შეკვეთები, წიგნის QA, მომხმარებლები, ბეჭდვა და მიწოდება",
  },
  {
    name: "Print Partner",
    people: "3 წევრი",
    access: "მხოლოდ მისთვის გაგზავნილი Print Job-ები და წარმოების სტატუსები",
  },
];

const integrations = [
  { name: "Payment Adapter", detail: "Provider-agnostic mock", status: "Mock", tone: "info" },
  { name: "Print Partner Portal", detail: "BookLab restricted workspace", status: "Connected", tone: "success" },
  { name: "Courier Adapter", detail: "Provider-agnostic mock + webhook states", status: "Mock", tone: "info" },
  { name: "Email / SMS", detail: "Notifications and OTP", status: "Not connected", tone: "neutral" },
];

export default function SettingsPage() {
  const adminState = useAdminState();
  const [toast, setToast] = useState("");
  const showToast = (message: string) => {
    setToast(message);
    window.setTimeout(() => setToast(""), 2400);
  };

  return (
    <AdminShell
      active="settings"
      title="Settings"
      subtitle="წვდომები, ინტეგრაციები, SLA და ოპერაციული წესები"
    >
      {toast && <div className="toast" role="status">{toast}</div>}
      <section className="settings-layout">
        <article className="panel partner-routing-card">
          <div>
            <p className="eyebrow">Primary operational channel</p>
            <h2>Print Partner შეკვეთებს იღებს ამავე Admin-ში, შეზღუდული როლით</h2>
            <p>
              Approved Print Job ავტომატურად ხვდება BookLab-ის Partner Inbox-ში.
              ელფოსტა გამოიყენება მხოლოდ როგორც backup შეტყობინება — სტატუსის
              მართვა და წარმოების სრული ისტორია რჩება Adventrya-ში.
            </p>
          </div>
          <div className="routing-flow">
            <span><strong>1</strong><small>Admin approves PDF</small></span>
            <i>→</i>
            <span><strong>2</strong><small>Partner Inbox + notification</small></span>
            <i>→</i>
            <span><strong>3</strong><small>Partner accepts in 4h</small></span>
            <i>→</i>
            <span><strong>4</strong><small>Packed unlocks courier</small></span>
          </div>
          <Link className="button button-primary" href="/partner">
            Partner Inbox-ის გახსნა
          </Link>
        </article>

        <article className="panel settings-card">
          <header className="panel-header">
            <div><p className="eyebrow">Access control</p><h2>როლები და უფლებები</h2></div>
            <button
              className="button button-secondary"
              onClick={() =>
                showToast("გუნდის წევრის დამატება backend integration-ის ეტაპზე ჩაირთვება")
              }
              type="button"
            >
              + გუნდის წევრი
            </button>
          </header>
          <div className="role-list">
            {roles.map((role) => (
              <section key={role.name}>
                <span className="settings-icon">◎</span>
                <span><strong>{role.name}</strong><small>{role.access}</small></span>
                <span>
                  <strong>{role.people}</strong>
                  <button
                    onClick={() =>
                      showToast(`${role.name} permission matrix ნაჩვენებია ქვემოთ`)
                    }
                    type="button"
                  >
                    უფლებები ↓
                  </button>
                </span>
              </section>
            ))}
          </div>
          <Link className="partner-access-link" href="/partner">
            <span><strong>Print Partner-ის შეზღუდული ხედვის Preview</strong><small>პარტნიორი ვერ ხედავს ბავშვის ფოტოს, ინტერესებსა და გადახდის მონაცემებს.</small></span>
            <span>გახსნა →</span>
          </Link>
          <div className="permission-matrix">
            <div className="permission-row permission-head">
              <span>მონაცემი / მოქმედება</span>
              <span>Admin</span>
              <span>Partner</span>
            </div>
            {[
              ["Order / Print Job ID", "✓", "✓"],
              ["დამტკიცებული PDF + hash", "✓", "✓"],
              ["წარმოების სპეციფიკაცია", "✓", "✓"],
              ["მშობლის ელფოსტა და ნომერი", "✓", "—"],
              ["მიწოდების მისამართი", "✓", "—"],
              ["ბავშვის ფოტო / Extra Wish", "✓", "—"],
              ["გადახდის მონაცემები", "✓", "—"],
              ["PDF approval", "✓", "—"],
              ["წარმოების სტატუსის შეცვლა", "✓", "✓"],
              ["კურიერის შექმნა", "✓", "—"],
            ].map(([label, admin, partner]) => (
              <div className="permission-row" key={label}>
                <span>{label}</span>
                <span className="permission-yes">{admin}</span>
                <span className={partner === "✓" ? "permission-yes" : "permission-no"}>
                  {partner}
                </span>
              </div>
            ))}
          </div>
        </article>

        <article className="panel settings-card">
          <header className="panel-header">
            <div><p className="eyebrow">System adapters</p><h2>ინტეგრაციები</h2></div>
          </header>
          <div className="integration-list">
            {integrations.map((integration) => (
              <section key={integration.name}>
                <span className="integration-mark">{integration.name.slice(0, 1)}</span>
                <span><strong>{integration.name}</strong><small>{integration.detail}</small></span>
                <span className={`integration-pill ${integration.tone}`}>{integration.status}</span>
                <button
                  className="icon-link"
                  onClick={() =>
                    showToast(`${integration.name} · ${integration.detail}`)
                  }
                  type="button"
                >
                  →
                </button>
              </section>
            ))}
          </div>
        </article>

        <article className="panel settings-card">
          <header className="panel-header">
            <div><p className="eyebrow">Operational targets</p><h2>SLA პარამეტრები</h2></div>
          </header>
          <div className="sla-settings">
            <label>Admin book review<input defaultValue="2 საათი" /></label>
            <label>Print partner acceptance<input defaultValue="4 საათი" /></label>
            <label>თბილისი · ბეჭდვა + მიწოდება<input defaultValue="4–5 სამუშაო დღე" /></label>
            <label>დანარჩენი საქართველო<input defaultValue="5–8 სამუშაო დღე" /></label>
          </div>
          <footer className="settings-footer">
            <p>ცვლილებები იმოქმედებს მხოლოდ ახალ შეკვეთებზე.</p>
            <button
              className="button button-primary"
              onClick={() => {
                adminState.recordAudit({
                  category: "ACCESS",
                  action: "ოპერაციული SLA პარამეტრები განახლდა",
                  detail:
                    "Admin review 2სთ · Partner acceptance 4სთ · თბილისი 4–5 დღე · რეგიონები 5–8 დღე",
                  actor: "ომიკო",
                  actorRole: "Super Admin",
                  timestamp: "ახლახან",
                  eventDate: "2026-07-28",
                  severity: "success",
                });
                showToast("SLA პარამეტრები შენახულია და Audit Log-ში ჩაიწერა");
              }}
              type="button"
            >
              შენახვა
            </button>
          </footer>
        </article>

        <article className="panel settings-card">
          <header className="panel-header">
            <div><p className="eyebrow">Immutable rules</p><h2>უსაფრთხო წარმოება</h2></div>
          </header>
          <div className="rules-list">
            <div><span>✓</span><p><strong>Print snapshot lock</strong><small>Approved PDF ვეღარ შეიცვლება ახალი ვერსიის გარეშე.</small></p></div>
            <div><span>✓</span><p><strong>Courier idempotency</strong><small>ერთ Print Job-ზე ორჯერ კურიერი ვერ შეიქმნება.</small></p></div>
            <div><span>✓</span><p><strong>PII minimization</strong><small>Print Partner ხედავს მხოლოდ წარმოებისთვის აუცილებელ მონაცემებს.</small></p></div>
            <div>
              <span>✓</span>
              <p>
                <strong>Audit trail</strong>
                <small>ყველა სტატუსი და ფინანსური მოქმედება ინახება append-only Audit Log-ში.</small>
              </p>
              <Link className="text-link" href="/audit">Audit Log →</Link>
            </div>
          </div>
        </article>
      </section>
    </AdminShell>
  );
}
