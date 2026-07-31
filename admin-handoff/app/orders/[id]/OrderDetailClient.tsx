"use client";

import Link from "next/link";
import { useMemo, useState } from "react";
import { AdminShell } from "../../components/AdminShell";
import {
  printJobStatusLabels,
  type PrintDocumentSnapshot,
  useAdminState,
} from "../../components/AdminState";
import { StatusBadge } from "../../components/StatusBadge";
import { orders } from "../../data";

type Tab =
  | "overview"
  | "book"
  | "generation"
  | "payment"
  | "fulfillment"
  | "activity";

const tabLabels: Array<[Tab, string]> = [
  ["overview", "Overview"],
  ["book", "Book & Characters"],
  ["generation", "Generation"],
  ["payment", "Payment"],
  ["fulfillment", "Print & Delivery"],
  ["activity", "Activity"],
];

const characterCards = [
  {
    name: "ზუკა",
    meta: "მთავარი გმირი · ბიჭი · 3 წლის",
    type: "child",
    initial: "ზ",
  },
  {
    name: "რექსი",
    meta: "ფანტაზიური მეგობარი · დინოზავრი",
    type: "fantasy",
    initial: "რ",
  },
];

const bookSpreads = [
  {
    left: "შიდა ყდა",
    right: "გვერდი 1",
    leftText: "ეს წიგნი შექმნილია პატარა გმირისთვის.",
    rightText: "ერთ დილას, როცა ტყე ჯერ კიდევ იღვიძებდა, ახალი გზა გამოჩნდა.",
  },
  {
    left: "გვერდი 2",
    right: "გვერდი 3",
    leftText: "მეგობრები კვალს გაჰყვნენ და ჩანჩქერის ხმა შორიდან მოესმათ.",
    rightText: "გზა რთული იყო, მაგრამ თითოეული ნაბიჯი მათ უფრო აახლოებდა.",
  },
  {
    left: "გვერდი 4",
    right: "გვერდი 5",
    leftText: "მოულოდნელად ხიდი გაქრა და გუნდს ახალი გამოსავალი უნდა ეპოვა.",
    rightText: "ზუკამ გაბედა, რექსმა კი აჩვენა, რომ ნდობა ყველაზე ძლიერი ძალაა.",
  },
  {
    left: "გვერდი 6",
    right: "გვერდი 7",
    leftText: "ზუკამ და რექსმა აღმოაჩინეს, რომ ყველაზე გრძელი გზაც მეგობრებთან ერთად იწყება.",
    rightText: "რექსმა იცოდა — ეს თავგადასავალი აქ არ სრულდებოდა.",
  },
];

export function OrderDetailClient({
  orderId,
  initialTab,
}: {
  orderId: string;
  initialTab?: string;
}) {
  const order = orders.find((item) => item.id === orderId) ?? orders[0];
  const validInitialTab = tabLabels.some(([value]) => value === initialTab)
    ? (initialTab as Tab)
    : "overview";
  const [tab, setTab] = useState<Tab>(validInitialTab);
  const [toast, setToast] = useState("");
  const [pageModal, setPageModal] = useState<number | null>(null);
  const [approvalModal, setApprovalModal] = useState(false);
  const [approvalConfirmed, setApprovalConfirmed] = useState(false);
  const [spreadIndex, setSpreadIndex] = useState(bookSpreads.length - 1);
  const [reason, setReason] = useState("");
  const [internalNote, setInternalNote] = useState("");
  const adminState = useAdminState();
  const printJob = adminState.printJobs.find((job) => job.orderId === order.id);
  const isApproved = Boolean(printJob?.approvedFileId);
  const candidateSnapshot = useMemo<PrintDocumentSnapshot>(
    () => ({
      fileId: `PDF-${order.id.replace("ADV-", "")}-V1`,
      fileName: `Adventrya_${order.id}_print_v1.pdf`,
      version: "v1",
      sha256: `SHA-256 · ${order.id.replace(/\D/g, "")}7be2a91c…8f20`,
      generatedAt: "დღეს, 14:48",
      size: "18.4 MB",
      preflightPassed: true,
    }),
    [order.id],
  );
  const displayedSnapshot: PrintDocumentSnapshot = printJob?.approvedFileId
    ? {
        ...candidateSnapshot,
        fileId: printJob.approvedFileId,
        fileName: printJob.approvedFileName ?? candidateSnapshot.fileName,
        version: printJob.approvedVersion ?? candidateSnapshot.version,
        sha256: printJob.approvedHash ?? candidateSnapshot.sha256,
      }
    : candidateSnapshot;

  const activity = useMemo(
    () => {
      const baseline = [
      {
        title: "Preview შეიქმნა",
        detail: "ყდა და პირველი გვერდი წარმატებით გენერირდა",
        time: "დღეს, 14:31",
        actor: "System",
      },
      {
        title: "შეკვეთა გადახდილია",
        detail: `${order.product} · ${order.price} · Apple Pay`,
        time: "დღეს, 14:42",
        actor: order.parentName,
      },
      {
        title: "სრული წიგნი გენერირდა",
        detail: "Cover + 7 illustrated pages · Automated check passed",
        time: "დღეს, 14:48",
        actor: "Book Engine",
      },
      ];
      const audit = adminState.auditEvents
        .filter((event) => event.orderId === order.id)
        .map((event) => ({
          title: event.action,
          detail: event.detail,
          time: event.timestamp,
          actor: `${event.actor} · ${event.actorRole}`,
        }));
      return [...baseline, ...audit];
    },
    [
      adminState.auditEvents,
      order.id,
      order.parentName,
      order.price,
      order.product,
    ],
  );

  const showToast = (message: string) => {
    setToast(message);
    window.setTimeout(() => setToast(""), 2800);
  };

  const approvePrint = () => {
    const result = adminState.approveForPrint(order.id, candidateSnapshot);
    setApprovalModal(false);
    setApprovalConfirmed(false);
    if (result === "approved") {
      showToast("ზუსტად ეს PDF snapshot დადასტურდა და პარტნიორს გაეგზავნა");
    } else if (result === "already-approved") {
      showToast("ეს PDF ვერსია უკვე დადასტურებულია");
    } else if (result === "revision-required") {
      showToast("არსებული Job აქტიურია — ახალი ვერსია ცალკე revision-ად უნდა გაიგზავნოს");
    } else {
      showToast("PDF-ის გაგზავნა დაიბლოკა — preflight არ არის დასრულებული");
    }
  };

  const recordAction = (
    category: "ORDER" | "BOOK" | "PAYMENT",
    action: string,
    detail: string,
    severity: "info" | "success" | "warning" | "danger" = "info",
  ) => {
    adminState.recordAudit({
      orderId: order.id,
      category,
      action,
      detail,
      actor: "ომიკო",
      actorRole: "Super Admin",
      timestamp: "ახლახან",
      eventDate: "2026-07-28",
      severity,
    });
  };

  const createCourier = () => {
    const result = adminState.createCourierOrder(order.id);
    const message =
      result === "creating"
        ? "საკურიერო შეკვეთა იქმნება — განმეორებითი მოთხოვნა დაბლოკილია"
        : result === "existing"
          ? "ამ Print Job-ზე საკურიერო შეკვეთა უკვე არსებობს"
          : result === "processing"
            ? "შექმნა უკვე მიმდინარეობს — მეორე მოთხოვნა არ გაიგზავნა"
            : "კურიერი მხოლოდ Packed სტატუსის შემდეგ იქმნება";
    showToast(message);
  };

  return (
    <AdminShell
      active="orders"
      title={`შეკვეთა ${order.id}`}
      subtitle={`${order.createdAt} · ${order.parentName}`}
      actions={
        <>
          <Link className="button button-secondary" href="/orders">
            ← შეკვეთებზე დაბრუნება
          </Link>
          <Link
            className="button button-secondary"
            href={`/audit?q=${encodeURIComponent(order.id)}`}
          >
            Audit history
          </Link>
        </>
      }
    >
      {toast && <div className="toast" role="status">{toast}</div>}

      <section className="status-strip" aria-label="შეკვეთის მიმდინარე სტატუსები">
        <div>
          <span>გადახდა</span>
          <StatusBadge value={order.paymentStatus} />
        </div>
        <i />
        <div>
          <span>გენერაცია</span>
          <StatusBadge value={order.generationStatus} />
        </div>
        <i />
        <div>
          <span>ბეჭდვა</span>
          {printJob ? (
            <span className="workflow-status">
              {printJobStatusLabels[printJob.status]}
            </span>
          ) : (
            <StatusBadge value={order.printStatus} />
          )}
        </div>
        <i />
        <div>
          <span>მიწოდება</span>
          <StatusBadge value={order.deliveryStatus} />
        </div>
      </section>

      <section className="panel order-detail-shell">
        <div className="detail-tabs" role="tablist" aria-label="შეკვეთის სექციები">
          {tabLabels.map(([value, label]) => (
            <button
              aria-selected={tab === value}
              className={tab === value ? "active" : ""}
              key={value}
              onClick={() => {
                setTab(value);
                window.history.replaceState(null, "", `?tab=${value}`);
              }}
              role="tab"
              type="button"
            >
              {label}
              {value === "generation" && order.generationStatus === "Review" && (
                <span className="tab-notice">1</span>
              )}
            </button>
          ))}
        </div>

        {tab === "overview" && (
          <div className="detail-body detail-overview">
            <article className="detail-card order-book-summary">
              <span className={`large-book-cover theme-${order.themeKey}`}>
                <small>ADVENTRYA</small>
                <strong>{order.bookTitle}</strong>
                <i>{order.childName.split("·")[0]}</i>
              </span>
              <div>
                <p className="eyebrow">Book order</p>
                <h2>{order.bookTitle}</h2>
                <dl className="inline-facts">
                  <div>
                    <dt>ფორმატი</dt>
                    <dd>{order.product}</dd>
                  </div>
                  <div>
                    <dt>თემა</dt>
                    <dd>{order.theme}</dd>
                  </div>
                  <div>
                    <dt>ენა</dt>
                    <dd>{order.bookLanguage}</dd>
                  </div>
                  <div>
                    <dt>ფასი</dt>
                    <dd>{order.price}</dd>
                  </div>
                </dl>
              </div>
            </article>

            <article className="detail-card">
              <header>
                <h3>მომხმარებელი</h3>
                <Link href="/customers">პროფილის გახსნა →</Link>
              </header>
              <dl className="detail-list">
                <div>
                  <dt>მშობელი</dt>
                  <dd>{order.parentName}</dd>
                </div>
                <div><dt>ელფოსტა</dt><dd>{order.email}</dd></div>
                <div><dt>ტელეფონი</dt><dd>{order.phone}</dd></div>
                <div>
                  <dt>ავტორიზაცია</dt>
                  <dd>Email Magic Link</dd>
                </div>
                <div>
                  <dt>შეკვეთები</dt>
                  <dd>3 შეკვეთა · 172 ₾</dd>
                </div>
              </dl>
            </article>

            <article className="detail-card">
              <header>
                <h3>მიწოდების ინფორმაცია</h3>
                <button
                  onClick={() =>
                    showToast(
                      "მისამართის ცვლილება დაშვებულია მხოლოდ courier order-ის შექმნამდე",
                    )
                  }
                  type="button"
                >
                  რედაქტირება
                </button>
              </header>
              <dl className="detail-list">
                <div>
                  <dt>მიმღები</dt>
                  <dd>{order.parentName}</dd>
                </div>
                <div>
                  <dt>ტელეფონი</dt>
                  <dd>{order.phone}</dd>
                </div>
                <div>
                  <dt>მისამართი</dt>
                  <dd>{order.city}, მისამართი სრულად დადასტურებულია</dd>
                </div>
                <div>
                  <dt>სავარაუდო მიწოდება</dt>
                  <dd>4–5 სამუშაო დღე</dd>
                </div>
              </dl>
            </article>

            <article className="detail-card">
              <header>
                <h3>რისკები და შენიშვნები</h3>
              </header>
              <div className="notice-card warning">
                <strong>Printed შეკვეთა ელოდება Admin Review-ს</strong>
                <p>PDF პარტნიორთან დადასტურებამდე არ გაიგზავნება.</p>
              </div>
              <textarea
                aria-label="შიდა შენიშვნა"
                onChange={(event) => setInternalNote(event.target.value)}
                placeholder="დამატეთ მხოლოდ გუნდისთვის ხილული შენიშვნა..."
                value={internalNote}
              />
              <button
                className="button button-secondary"
                disabled={!internalNote.trim()}
                onClick={() => {
                  recordAction(
                    "ORDER",
                    "შიდა შენიშვნა დაემატა",
                    internalNote.trim(),
                  );
                  setInternalNote("");
                  showToast("შიდა შენიშვნა შენახულია და Audit Log-ში ჩაიწერა");
                }}
                type="button"
              >
                შენიშვნის შენახვა
              </button>
            </article>
          </div>
        )}

        {tab === "book" && (
          <div className="detail-body book-tab-layout">
            <article className="storybook-review">
              <div className="review-book">
                <div className={`review-page theme-${order.themeKey}`}>
                  <span className="page-number">{bookSpreads[spreadIndex].left}</span>
                  <div className="illustration-placeholder">
                    <span>ილუსტრაცია</span>
                  </div>
                  <p>{bookSpreads[spreadIndex].leftText}</p>
                </div>
                <div className={`review-page theme-${order.themeKey}`}>
                  <span className="page-number">{bookSpreads[spreadIndex].right}</span>
                  <div className="illustration-placeholder final-page">
                    <span>
                      {spreadIndex === bookSpreads.length - 1
                        ? "QR · გაგრძელება"
                        : "ილუსტრაცია"}
                    </span>
                  </div>
                  <p>{bookSpreads[spreadIndex].rightText}</p>
                </div>
              </div>
              <div className="book-controls">
                <button
                  disabled={spreadIndex === 0}
                  onClick={() => setSpreadIndex((current) => Math.max(0, current - 1))}
                  type="button"
                >
                  ←
                </button>
                <span>
                  Spread {spreadIndex + 1} / {bookSpreads.length}
                </span>
                <button
                  disabled={spreadIndex === bookSpreads.length - 1}
                  onClick={() =>
                    setSpreadIndex((current) =>
                      Math.min(bookSpreads.length - 1, current + 1),
                    )
                  }
                  type="button"
                >
                  →
                </button>
              </div>
            </article>

            <aside className="book-review-side">
              <article className="detail-card">
                <header>
                  <h3>პერსონაჟები</h3>
                  <span>2</span>
                </header>
                <div className="character-list">
                  {characterCards.map((character) => (
                    <div className="character-row" key={character.name}>
                      <span className={`character-avatar ${character.type}`}>
                        {character.initial}
                      </span>
                      <span>
                        <strong>{character.name}</strong>
                        <small>{character.meta}</small>
                      </span>
                    </div>
                  ))}
                </div>
              </article>
              <article className="detail-card">
                <header>
                  <h3>Story input</h3>
                </header>
                <dl className="detail-list">
                  <div>
                    <dt>ინტერესები</dt>
                    <dd>დინოზავრები, მოგზაურობა, მეგობრობა</dd>
                  </div>
                  <div>
                    <dt>Extra Wish</dt>
                    <dd>
                      რექსმა ზუკა ზურგზე შემოისვას და დაკარგული ჩანჩქერი
                      იპოვონ.
                    </dd>
                  </div>
                  <div>
                    <dt>ისტორიის დონე</dt>
                    <dd>2–4 წლის</dd>
                  </div>
                </dl>
              </article>
              <button
                className="button button-secondary full-button"
                onClick={() => setTab("fulfillment")}
                type="button"
              >
                Print-ready PDF-ის ნახვა
              </button>
            </aside>
          </div>
        )}

        {tab === "generation" && (
          <div className="detail-body generation-layout">
            <article className="detail-card">
              <header>
                <div>
                  <p className="eyebrow">Automated preflight</p>
                  <h3>ტექნიკური შემოწმება</h3>
                </div>
                <span className="preflight-score">7/7 Passed</span>
              </header>
              <div className="check-list">
                {[
                  "Cover + 7 გვერდი",
                  "სურათების მინიმალური ხარისხი",
                  "ტექსტის უსაფრთხო ზონა",
                  "QR კოდის წაკითხვადობა",
                  "ფონტები ჩაშენებულია",
                  "Bleed და trim ზომები",
                  "ბავშვის სახის consistency",
                ].map((check) => (
                  <div key={check}>
                    <span>✓</span>
                    {check}
                  </div>
                ))}
              </div>
            </article>

            <article className="detail-card page-review-card">
              <header>
                <div>
                  <p className="eyebrow">Page control</p>
                  <h3>გვერდების შემოწმება</h3>
                </div>
                <span className="cost-note">Estimated generation cost: 5.20 ₾</span>
              </header>
              <div className="page-status-grid">
                {[0, 1, 2, 3, 4, 5, 6, 7].map((page) => (
                  <button
                    className={page === 4 ? "needs-attention" : ""}
                    key={page}
                    onClick={() => setPageModal(page)}
                    type="button"
                  >
                    <span className={`mini-page theme-${order.themeKey}`}>
                      {page === 0 ? "ყდა" : page}
                    </span>
                    <small>{page === 4 ? "Review note" : "Passed"}</small>
                  </button>
                ))}
              </div>
            </article>

            <article className="detail-card generation-history">
              <header>
                <h3>გენერაციის ისტორია</h3>
              </header>
              <div className="timeline-rows">
                <div><span>14:43</span><strong>Story generated</strong><small>1m 14s</small></div>
                <div><span>14:44</span><strong>Illustrations generated</strong><small>3m 02s</small></div>
                <div><span>14:47</span><strong>Book assembled</strong><small>38s</small></div>
                <div><span>14:48</span><strong>Automated checks passed</strong><small>7/7</small></div>
              </div>
            </article>
          </div>
        )}

        {tab === "payment" && (
          <div className="detail-body payment-layout">
            <article className="detail-card">
              <header>
                <h3>Order Summary</h3>
                <StatusBadge value={order.paymentStatus} />
              </header>
              <dl className="price-breakdown">
                <div><dt>Printed + Digital</dt><dd>79.00 ₾</dd></div>
                <div><dt>მიწოდება საქართველოში</dt><dd>შედის ფასში</dd></div>
                <div><dt>Promocode · MAGIC20</dt><dd>−15.80 ₾</dd></div>
                <div className="total"><dt>საბოლოო თანხა</dt><dd>63.20 ₾</dd></div>
              </dl>
            </article>
            <article className="detail-card">
              <header><h3>Payment record</h3></header>
              <dl className="detail-list">
                <div><dt>Transaction ID</dt><dd>TXN-9E42•••18</dd></div>
                <div><dt>მეთოდი</dt><dd>Apple Pay · Mastercard ••42</dd></div>
                <div><dt>Provider</dt><dd>Mock Provider Adapter</dd></div>
                <div><dt>თარიღი</dt><dd>28 ივლისი, 14:42</dd></div>
              </dl>
            </article>
            <article className="detail-card payment-actions-card">
              <header><h3>Payment actions</h3></header>
              <p>
                ფინანსური მოქმედება Audit Log-ში ჩაიწერება და საჭიროებს
                Super Admin უფლებას.
              </p>
              <div>
                <button
                  className="button button-secondary"
                  onClick={() => {
                    recordAction(
                      "PAYMENT",
                      "თანხის დაბრუნება მოთხოვნილია",
                      `Refund review · ${order.price} · საბოლოო შესრულებამდე საჭიროა ფინანსური დადასტურება`,
                      "warning",
                    );
                    showToast("Refund მოთხოვნა Audit Log-ში ჩაიწერა");
                  }}
                  type="button"
                >
                  თანხის დაბრუნება
                </button>
                <button
                  className="button button-danger"
                  onClick={() => {
                    recordAction(
                      "PAYMENT",
                      "შეკვეთის გაუქმება მოთხოვნილია",
                      `Order cancellation review · ${order.price}`,
                      "danger",
                    );
                    showToast("გაუქმების მოთხოვნა Audit Log-ში ჩაიწერა");
                  }}
                  type="button"
                >
                  შეკვეთის გაუქმება
                </button>
              </div>
            </article>
          </div>
        )}

        {tab === "fulfillment" && (
          <div className="detail-body fulfillment-tab">
            <article className="detail-card approval-card">
              <header>
                <div>
                  <p className="eyebrow">Step 1</p>
                  <h3>საბეჭდი ვერსიის დადასტურება</h3>
                </div>
                <span className={isApproved ? "approval-success" : "approval-pending"}>
                  {isApproved ? "Approved" : "Waiting for review"}
                </span>
              </header>
              <div className="approval-content">
                <span className={`print-file-preview theme-${order.themeKey}`}>
                  <small>PRINT PDF</small>
                  <strong>{order.bookTitle}</strong>
                  <i>Cover + 7 pages</i>
                </span>
                <div>
                  <h4>{displayedSnapshot.fileName}</h4>
                  <p>
                    {displayedSnapshot.fileId} · {displayedSnapshot.version} ·{" "}
                    {displayedSnapshot.size}
                  </p>
                  <ul>
                    <li>Automated preflight passed</li>
                    <li>QR code verified</li>
                    <li>{displayedSnapshot.sha256}</li>
                  </ul>
                </div>
                <div className="approval-actions">
                  <button
                    className="button button-secondary"
                    onClick={() => setApprovalModal(true)}
                    type="button"
                  >
                    PDF-ის ნახვა
                  </button>
                  <button
                    className="button button-primary"
                    disabled={isApproved}
                    onClick={() => setApprovalModal(true)}
                    type="button"
                  >
                    {isApproved
                      ? "პარტნიორთან გაგზავნილია"
                      : "დადასტურება და გაგზავნა"}
                  </button>
                </div>
              </div>
              {printJob?.approvedFileId && (
                <div className="immutable-snapshot">
                  <span className="immutable-lock">🔒</span>
                  <span>
                    <strong>დაბლოკილი საბეჭდი snapshot</strong>
                    <small>
                      {printJob.approvedFileName} · {printJob.approvedFileId}
                    </small>
                  </span>
                  <span>
                    <strong>{printJob.approvedVersion}</strong>
                    <small>{printJob.approvedHash}</small>
                  </span>
                  <span>
                    <strong>{printJob.approvedBy}</strong>
                    <small>{printJob.approvedAt}</small>
                  </span>
                </div>
              )}
            </article>

            <article className="detail-card">
              <header>
                <div>
                  <p className="eyebrow">Step 2</p>
                  <h3>Print Job</h3>
                </div>
                <span className="muted-label">{printJob?.id ?? "ჯერ არ შექმნილა"}</span>
              </header>
              {printJob ? (
                <div className="print-job-detail-block">
                  <dl className="detail-list horizontal-list">
                    <div><dt>პარტნიორი</dt><dd>BookLab · თბილისი</dd></div>
                    <div><dt>სტატუსი</dt><dd>{printJobStatusLabels[printJob.status]}</dd></div>
                    <div><dt>ფაილი</dt><dd>{printJob.approvedFileName ?? printJob.version}</dd></div>
                    <div><dt>Due date</dt><dd>{printJob.dueAt}</dd></div>
                    <div><dt>Current owner</dt><dd>{printJob.owner}</dd></div>
                    <div><dt>Last update</dt><dd>{printJob.lastUpdatedAt}</dd></div>
                    <div><dt>SLA</dt><dd>{printJob.sla}</dd></div>
                    <div><dt>Version</dt><dd>{printJob.approvedVersion ?? "—"}</dd></div>
                    <div><dt>Hash</dt><dd>{printJob.approvedHash ?? "—"}</dd></div>
                  </dl>
                  <div className="handoff-receipt">
                    <span>✓ Immutable PDF snapshot</span>
                    <span>✓ Partner Inbox routing</span>
                    <span>{printJob.sentAt ? `✓ Sent · ${printJob.sentAt}` : "○ გაგზავნას ელოდება"}</span>
                  </div>
                </div>
              ) : (
                <div className="inline-empty">
                  Print Job ავტომატურად შეიქმნება PDF-ის დადასტურების შემდეგ.
                </div>
              )}
            </article>

            <article className="detail-card courier-card">
              <header>
                <div>
                  <p className="eyebrow">Step 3</p>
                  <h3>საკურიერო შეკვეთა</h3>
                </div>
                <span className="muted-label">
                  {printJob?.courierStatus === "creating"
                    ? "იქმნება…"
                    : printJob?.courierCreated
                      ? printJob.trackingId
                      : "არ შექმნილა"}
                </span>
              </header>
              <div className="courier-grid">
                <label>მიმღები<input defaultValue={order.parentName} /></label>
                <label>ტელეფონი<input defaultValue={order.phone} /></label>
                <label className="span-two">ელფოსტა<input defaultValue={order.email} /></label>
                <label className="span-two">მისამართი<input defaultValue={`${order.city}, დადასტურებული მისამართი`} /></label>
                <label>Pickup<input defaultValue="BookLab · თბილისი" /></label>
                <label>პაკეტი<input defaultValue="1 წიგნი · 0.6 კგ" /></label>
                <label className="span-two">კურიერისთვის შენიშვნა<input defaultValue="დარეკეთ მისვლამდე" /></label>
              </div>
              <div className="courier-footer">
                <p>
                  კურიერის რეალური შეკვეთა შეიქმნება მხოლოდ Packed სტატუსის
                  შემდეგ.
                </p>
                <button
                  className="button button-primary"
                  disabled={
                    !printJob ||
                    printJob.status !== "Packed" ||
                    printJob.courierCreated ||
                    printJob.courierStatus === "creating"
                  }
                  onClick={createCourier}
                  type="button"
                >
                  {printJob?.courierStatus === "creating"
                    ? "საკურიერო შეკვეთა იქმნება…"
                    : printJob?.courierCreated
                      ? "საკურიერო შეკვეთა შექმნილია"
                      : "საკურიერო შეკვეთის შექმნა"}
                </button>
              </div>
              {printJob?.courierCreated && (
                <div className="courier-receipt">
                  <span>
                    <strong>Courier order</strong>
                    <small>{printJob.externalCourierOrderId}</small>
                  </span>
                  <span>
                    <strong>Tracking</strong>
                    <small>{printJob.trackingId}</small>
                  </span>
                  <span>
                    <strong>Idempotency key</strong>
                    <small>{printJob.courierIdempotencyKey}</small>
                  </span>
                </div>
              )}
            </article>
          </div>
        )}

        {tab === "activity" && (
          <div className="detail-body activity-tab">
            <article className="detail-card">
              <header><h3>Activity Log</h3><span>ყველა დრო ნაჩვენებია GET</span></header>
              <div className="activity-list">
                {[...activity].reverse().map((item) => (
                  <div key={`${item.title}-${item.time}`}>
                    <span className="activity-dot" />
                    <span>
                      <strong>{item.title}</strong>
                      <small>{item.detail}</small>
                    </span>
                    <span className="activity-actor">{item.actor}</span>
                    <time>{item.time}</time>
                  </div>
                ))}
              </div>
            </article>
          </div>
        )}
      </section>

      {pageModal !== null && (
        <div className="modal-backdrop" role="presentation">
          <section aria-modal="true" className="modal" role="dialog">
            <header>
              <div>
                <p className="eyebrow">Page control</p>
                <h2>{pageModal === 0 ? "ყდა" : `გვერდი ${pageModal}`}</h2>
              </div>
              <button
                aria-label="ფანჯრის დახურვა"
                onClick={() => setPageModal(null)}
                type="button"
              >
                ×
              </button>
            </header>
            <div className="modal-page-preview">
              <span className={`mini-page theme-${order.themeKey}`}>
                {pageModal === 0 ? "ყდა" : pageModal}
              </span>
              <div>
                <strong>Automated checks passed</strong>
                <p>Visual consistency score 94% · Text safety 100%</p>
              </div>
            </div>
            <label>
              რეგენერაციის მიზეზი
              <select value={reason} onChange={(event) => setReason(event.target.value)}>
                <option value="">აირჩიეთ მიზეზი</option>
                <option>ბავშვს არ ჰგავს</option>
                <option>არასწორი პერსონაჟი</option>
                <option>ვიზუალური შეუსაბამობა</option>
                <option>ტექსტის შეცდომა</option>
                <option>დაზიანებული კომპოზიცია</option>
                <option>სხვა</option>
              </select>
            </label>
            <div className="cost-warning">
              Estimated regeneration cost: <strong>0.65 ₾</strong>
            </div>
            <footer>
              <button
                className="button button-secondary"
                onClick={() => setPageModal(null)}
                type="button"
              >
                გაუქმება
              </button>
              <button
                className="button button-primary"
                disabled={!reason}
                onClick={() => {
                  recordAction(
                    "BOOK",
                    "გვერდის რეგენერაცია მოთხოვნილია",
                    `გვერდი ${pageModal} · მიზეზი: ${reason}. არსებული დამტკიცებული PDF უცვლელია; ახალი შედეგი ცალკე revision-ად მოითხოვს approval-ს.`,
                    "warning",
                  );
                  setPageModal(null);
                  showToast("გვერდი დაემატა რეგენერაციის queue-ში");
                }}
                type="button"
              >
                მხოლოდ ამ გვერდის რეგენერაცია
              </button>
            </footer>
          </section>
        </div>
      )}

      {approvalModal && (
        <div className="modal-backdrop" role="presentation">
          <section
            aria-labelledby="approval-title"
            aria-modal="true"
            className="modal approval-confirmation-modal"
            role="dialog"
          >
            <header>
              <div>
                <p className="eyebrow">Immutable production handoff</p>
                <h2 id="approval-title">ზუსტად ამ PDF-ის დადასტურება</h2>
              </div>
              <button
                aria-label="ფანჯრის დახურვა"
                onClick={() => {
                  setApprovalModal(false);
                  setApprovalConfirmed(false);
                }}
                type="button"
              >
                ×
              </button>
            </header>
            <div className="approval-file-identity">
              <span className={`print-file-preview theme-${order.themeKey}`}>
                <small>PRINT PDF</small>
                <strong>{order.bookTitle}</strong>
                <i>{displayedSnapshot.version}</i>
              </span>
              <dl>
                <div><dt>ფაილი</dt><dd>{displayedSnapshot.fileName}</dd></div>
                <div><dt>File ID</dt><dd>{displayedSnapshot.fileId}</dd></div>
                <div><dt>ვერსია</dt><dd>{displayedSnapshot.version}</dd></div>
                <div><dt>Hash</dt><dd>{displayedSnapshot.sha256}</dd></div>
                <div>
                  <dt>Preflight</dt>
                  <dd className="success-text">Passed · 7/7</dd>
                </div>
              </dl>
            </div>
            <div className="notice-card warning">
              დადასტურების შემდეგ პარტნიორი მიიღებს მხოლოდ ამ snapshot-ს.
              ახალი რეგენერაცია მას ავტომატურად ვერ ჩაანაცვლებს.
            </div>
            <label className="approval-checkbox">
              <input
                checked={approvalConfirmed}
                onChange={(event) => setApprovalConfirmed(event.target.checked)}
                type="checkbox"
              />
              <span>
                შევამოწმე ფაილის სახელი, ვერსია და ვიზუალი; დასაბეჭდად ვადასტურებ
                სწორედ ამ snapshot-ს.
              </span>
            </label>
            <footer>
              <button
                className="button button-secondary"
                onClick={() => setApprovalModal(false)}
                type="button"
              >
                დაბრუნება
              </button>
              <button
                className="button button-primary"
                disabled={!approvalConfirmed || isApproved}
                onClick={approvePrint}
                type="button"
              >
                PDF-ის ჩაკეტვა და Partner-თან გაგზავნა
              </button>
            </footer>
          </section>
        </div>
      )}
    </AdminShell>
  );
}
