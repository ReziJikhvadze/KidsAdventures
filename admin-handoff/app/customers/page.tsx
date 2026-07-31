"use client";

import Link from "next/link";
import { useMemo, useState } from "react";
import { AdminShell } from "../components/AdminShell";
import { useAdminState } from "../components/AdminState";
import {
  DateRangeFilter,
  inDateRange,
  type DateRange,
} from "../components/DateRangeFilter";
import { orders } from "../data";

const customers = [
  {
    id: "CUS-0842",
    name: "თამარ გოგიშვილი",
    email: "tamar.gogishvili@gmail.com",
    phone: "+995 555 12 34 42",
    auth: "Email",
    joined: "12 მაისი, 2026",
    joinedDate: "2026-05-12",
    orders: 3,
    spend: "172 ₾",
    children: [
      { name: "ზუკა", meta: "3 წლის · ბიჭი", books: 2 },
      { name: "ნინი", meta: "6 წლის · გოგო", books: 1 },
    ],
  },
  {
    id: "CUS-0841",
    name: "ნათია კაპანაძე",
    email: "natia.kapanadze@icloud.com",
    phone: "+995 599 43 21 18",
    auth: "Apple",
    joined: "9 ივნისი, 2026",
    joinedDate: "2026-06-09",
    orders: 1,
    spend: "14 ₾",
    children: [{ name: "ნინი", meta: "6 წლის · გოგო", books: 1 }],
  },
  {
    id: "CUS-0839",
    name: "გიორგი მელაძე",
    email: "giorgi.meladze@gmail.com",
    phone: "+995 577 65 44 41",
    auth: "Phone",
    joined: "18 ივნისი, 2026",
    joinedDate: "2026-06-18",
    orders: 2,
    spend: "79 ₾",
    children: [{ name: "ელისო", meta: "5 წლის · გოგო", books: 1 }],
  },
  {
    id: "CUS-0838",
    name: "ანა დოლიძე",
    email: "ana.dolidze@gmail.com",
    phone: "+995 551 74 20 06",
    auth: "Google",
    joined: "2 ივლისი, 2026",
    joinedDate: "2026-07-02",
    orders: 1,
    spend: "79 ₾",
    children: [{ name: "ლუკა", meta: "8 წლის · ბიჭი", books: 1 }],
  },
];

export default function CustomersPage() {
  const adminState = useAdminState();
  const [query, setQuery] = useState("");
  const [selectedId, setSelectedId] = useState(customers[0].id);
  const [noteOpen, setNoteOpen] = useState(false);
  const [note, setNote] = useState("");
  const [toast, setToast] = useState("");
  const [dateRange, setDateRange] = useState<DateRange>({
    from: "2026-05-01",
    to: "2026-07-28",
    label: "არჩეული პერიოდი",
  });
  const filtered = useMemo(
    () =>
      customers.filter(
        (customer) =>
          `${customer.name} ${customer.email} ${customer.phone} ${customer.children.map((child) => child.name).join(" ")}`
            .toLowerCase()
            .includes(query.toLowerCase()) &&
          inDateRange(customer.joinedDate, dateRange),
      ),
    [dateRange, query],
  );
  const selected =
    filtered.find((customer) => customer.id === selectedId) ??
    filtered[0] ??
    customers[0];
  const selectedOrders = orders.filter(
    (order) => order.email === selected.email,
  );

  const showToast = (message: string) => {
    setToast(message);
    window.setTimeout(() => setToast(""), 2400);
  };

  return (
    <AdminShell
      active="customers"
      title="მომხმარებლები"
      subtitle="მშობლები, ბავშვების პროფილები, სრული საკონტაქტო მონაცემები და წიგნების ისტორია"
    >
      {toast && <div className="toast" role="status">{toast}</div>}
      <DateRangeFilter
        initialFrom="2026-05-01"
        initialLabel="არჩეული პერიოდი"
        label="რეგისტრაციის პერიოდი"
        onApply={setDateRange}
      />

      <section className="customer-layout">
        <article className="panel customer-list-panel">
          <header className="panel-header">
            <div><p className="eyebrow">Parent accounts</p><h2>{filtered.length} მომხმარებელი</h2></div>
            <input
              aria-label="მომხმარებლის ძებნა"
              onChange={(event) => setQuery(event.target.value)}
              placeholder="მშობელი, ბავშვი, ელფოსტა..."
              type="search"
              value={query}
            />
          </header>
          <div className="customer-list">
            {filtered.map((customer) => (
              <button
                className={selected.id === customer.id ? "selected" : ""}
                key={customer.id}
                onClick={() => setSelectedId(customer.id)}
                type="button"
              >
                <span className="customer-avatar">{customer.name.slice(0, 1)}</span>
                <span>
                  <strong>{customer.name}</strong>
                  <small>{customer.email}</small>
                  <small>{customer.phone}</small>
                </span>
                <span>
                  <strong>{customer.orders}</strong>
                  <small>შეკვეთა</small>
                </span>
              </button>
            ))}
          </div>
        </article>

        <article className="panel customer-profile">
          <header className="customer-profile-head">
            <span className="customer-avatar large">{selected.name.slice(0, 1)}</span>
            <div>
              <p className="eyebrow">{selected.id}</p>
              <h2>{selected.name}</h2>
              <p>{selected.email} · {selected.phone}</p>
            </div>
            <button
              className="button button-secondary"
              onClick={() => setNoteOpen((open) => !open)}
              type="button"
            >
              შიდა შენიშვნა
            </button>
          </header>
          {noteOpen && (
            <div className="customer-note-composer">
              <label>
                მხოლოდ Adventrya-ს გუნდისთვის
                <textarea
                  autoFocus
                  onChange={(event) => setNote(event.target.value)}
                  placeholder="დაწერეთ კონტექსტი, რომელიც მომავალ მხარდაჭერას გამოადგება…"
                  value={note}
                />
              </label>
              <div>
                <button
                  className="button button-secondary"
                  onClick={() => {
                    setNoteOpen(false);
                    setNote("");
                  }}
                  type="button"
                >
                  გაუქმება
                </button>
                <button
                  className="button button-primary"
                  disabled={!note.trim()}
                  onClick={() => {
                    adminState.recordAudit({
                      category: "ORDER",
                      action: "მომხმარებლის შიდა შენიშვნა დაემატა",
                      detail: `${selected.id} · ${selected.name} · ${note.trim()}`,
                      actor: "ომიკო",
                      actorRole: "Super Admin",
                      timestamp: "ახლახან",
                      eventDate: "2026-07-28",
                      severity: "info",
                    });
                    setNoteOpen(false);
                    setNote("");
                    showToast("შენიშვნა შენახულია და Audit Log-ში ჩაიწერა");
                  }}
                  type="button"
                >
                  შენახვა
                </button>
              </div>
            </div>
          )}
          <dl className="customer-facts">
            <div><dt>ავტორიზაცია</dt><dd>{selected.auth}</dd></div>
            <div><dt>რეგისტრაცია</dt><dd>{selected.joined}</dd></div>
            <div><dt>შეკვეთები</dt><dd>{selected.orders}</dd></div>
            <div><dt>სრული ხარჯი</dt><dd>{selected.spend}</dd></div>
          </dl>

          <section className="profile-section">
            <header><h3>ბავშვების პროფილები</h3><span>{selected.children.length}</span></header>
            <div className="child-profile-grid">
              {selected.children.map((child) => (
                <article key={child.name}>
                  <span className="character-avatar">{child.name.slice(0, 1)}</span>
                  <span><strong>{child.name}</strong><small>{child.meta}</small></span>
                  <span><strong>{child.books}</strong><small>წიგნი</small></span>
                </article>
              ))}
            </div>
          </section>

          <section className="profile-section">
            <header>
              <h3>ბოლო წიგნები და შეკვეთები</h3>
              <Link href={`/orders?q=${encodeURIComponent(selected.email)}`}>
                სრული ისტორია →
              </Link>
            </header>
            <div className="customer-books">
              {selectedOrders.map((order) => (
                <article key={order.id}>
                  <span className={`book-thumb theme-${order.themeKey}`}>{order.initial}</span>
                  <span>
                    <strong>{order.bookTitle}</strong>
                    <small>{order.id} · {order.product}</small>
                  </span>
                  <span className="customer-book-actions">
                    {order.product === "Digital" && (
                      <Link href={`/orders/${order.id}?tab=payment`}>
                        Print upgrade · 65 ₾
                      </Link>
                    )}
                    <Link href={`/orders/${order.id}`}>გახსნა →</Link>
                  </span>
                </article>
              ))}
              {!selectedOrders.length && (
                <div className="table-empty">
                  ამ მომხმარებლის შეკვეთა მიმდინარე mock data-ში არ არის.
                </div>
              )}
            </div>
          </section>
        </article>
      </section>
    </AdminShell>
  );
}
