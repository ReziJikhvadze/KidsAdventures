import { createFileRoute, useNavigate } from "@tanstack/react-router";
import { useCallback, useEffect, useRef, useState } from "react";

import {
  AdminPanel,
  AdminScreen,
  confirmSpend,
  statusDot,
  useAdminData,
  useDebounced,
} from "@/components/admin/AdminScreen";
import { announceAlarmsChanged } from "@/components/admin/AdminShell";
import {
  BOOK_STATUS_TEXT,
  CLASS_TEXT,
  GATE_STATUS_TEXT,
  ORDER_STATUSES,
  ORDER_STATUS_TEXT,
  ORDER_TYPE_TEXT,
  PACKAGE_TEXT,
  PIPELINE_TEXT,
  PRINT_STATUSES,
  PRINT_STATUS_TEXT,
  PROVIDER_TEXT,
  RESOLUTION_TEXT,
  WORLD_TEXT,
  checkLabel,
  label,
  severityText,
} from "@/components/admin/labels";
import * as admin from "@/lib/api/admin";
import { BRAND_NAME } from "@/lib/brand";
import { buildPageMeta } from "@/lib/seo";

type OrdersSearch = { q?: string; status?: string; flag?: string };

const FLAGS = [
  admin.NEEDS_ATTENTION,
  admin.PAID_UNFULFILLED,
  admin.GENERATING,
  admin.STUCK,
  admin.AWAITING_REVIEW,
  admin.FAILED_BOOKS,
];

const FLAG_TEXT: Record<string, string> = {
  [admin.NEEDS_ATTENTION]: "საჭიროებს ყურადღებას",
  [admin.PAID_UNFULFILLED]: "გადახდილი, შეუსრულებელი",
  [admin.GENERATING]: "ახლა იხატება",
  [admin.STUCK]: "გაჩერებული",
  [admin.AWAITING_REVIEW]: "ელოდება შემოწმებას",
  [admin.FAILED_BOOKS]: "ჩავარდნილი წიგნები",
};

export const Route = createFileRoute("/admin/orders")({
  // Declared rather than read off window.location. The alert emails deep-link here with
  // ?q={orderId}, the overview links with a flag, and a refresh has to keep what was typed.
  validateSearch: (search: Record<string, unknown>): OrdersSearch => ({
    q: typeof search.q === "string" && search.q.length > 0 ? search.q : undefined,
    status:
      typeof search.status === "string" && ORDER_STATUSES.includes(search.status)
        ? search.status
        : undefined,
    flag: typeof search.flag === "string" && FLAGS.includes(search.flag) ? search.flag : undefined,
  }),
  head: () => {
    const { meta, links } = buildPageMeta({
      title: `შეკვეთები — ${BRAND_NAME} Admin`,
      description: "Orders.",
      path: "/admin/orders",
      noindex: true,
    });
    return { meta, links };
  },
  component: OrdersPage,
});

const PAGE_SIZE = 25;

function OrdersPage() {
  const { q, status, flag } = Route.useSearch();
  const navigate = useNavigate({ from: "/admin/orders" });

  const [search, setSearch] = useState(q ?? "");
  const debouncedSearch = useDebounced(search, 300);
  const [page, setPage] = useState(1);
  const [openId, setOpenId] = useState<string | null>(null);

  // A link from elsewhere arrives with a new ?q while this component is already mounted.
  useEffect(() => {
    setSearch(q ?? "");
    setPage(1);
  }, [q]);

  // What was typed goes back into the address, so a refresh does not lose it.
  useEffect(() => {
    if ((q ?? "") === debouncedSearch) return;
    void navigate({
      search: (prev) => ({ ...prev, q: debouncedSearch || undefined }),
      replace: true,
    });
  }, [debouncedSearch, q, navigate]);

  const setStatus = (next: string) =>
    void navigate({ search: (prev) => ({ ...prev, status: next || undefined }), replace: true });
  const setFlag = (next: string | undefined) =>
    void navigate({ search: (prev) => ({ ...prev, flag: next }), replace: true });

  const { data, error, busy, refreshing, reload } = useAdminData(
    () =>
      admin.listOrders({
        status: status || undefined,
        search: debouncedSearch || undefined,
        flag,
        page,
        pageSize: PAGE_SIZE,
      }),
    [status, debouncedSearch, flag, page],
  );

  const lastPage = Math.max(1, Math.ceil((data?.total ?? 0) / PAGE_SIZE));
  const openRow = data?.items.find((o) => o.id === openId) ?? null;

  return (
    <AdminScreen active="orders" title="შეკვეთები" subtitle="ყველა მომხმარებლის შეკვეთა">
      <div className="panel orders-workspace">
        <div className="orders-toolbar">
          <input
            type="search"
            placeholder="ელფოსტა, ტელეფონი, სახელი, გმირი, წიგნი ან Order ID…"
            value={search}
            onChange={(e) => {
              setSearch(e.target.value);
              setPage(1);
            }}
            aria-label="ძებნა"
          />
          <select
            value={status ?? ""}
            onChange={(e) => {
              setStatus(e.target.value);
              setPage(1);
            }}
            aria-label="სტატუსი"
          >
            <option value="">ყველა სტატუსი</option>
            {ORDER_STATUSES.map((s) => (
              <option key={s} value={s}>
                {ORDER_STATUS_TEXT[s]}
              </option>
            ))}
          </select>
          {data ? <span className="filter-count">{data.total}</span> : null}
          {refreshing ? <span className="cell-subtitle">ახლდება…</span> : null}
        </div>
        <div className="orders-toolbar quick-filter-row">
          {FLAGS.map((f) => (
            <button
              key={f}
              type="button"
              className={`button ${flag === f ? "" : "button-secondary"}`}
              aria-pressed={flag === f}
              onClick={() => {
                setFlag(flag === f ? undefined : f);
                setPage(1);
              }}
            >
              {FLAG_TEXT[f]}
            </button>
          ))}
        </div>

        <AdminPanel error={error} busy={busy} empty={data?.items.length === 0}>
          {data && data.items.length > 0 ? (
            <>
              <div className="table-scroll">
                <table className="data-table orders-table">
                  <thead>
                    <tr>
                      <th>თარიღი</th>
                      <th>მომხმარებელი</th>
                      <th>წიგნი</th>
                      <th>პაკეტი</th>
                      <th>სტატუსი</th>
                      <th>ჯამი</th>
                    </tr>
                  </thead>
                  <tbody>
                    {data.items.map((o) => (
                      <OrderRow
                        key={o.id}
                        order={o}
                        open={openId === o.id}
                        onToggle={() => setOpenId(openId === o.id ? null : o.id)}
                      />
                    ))}
                  </tbody>
                </table>
              </div>

              <div className="filter-context">
                <button
                  className="button button-secondary"
                  type="button"
                  disabled={page <= 1}
                  onClick={() => setPage(page - 1)}
                >
                  წინა
                </button>
                <span>
                  გვერდი {page} / {lastPage} · {data.total} ჩანაწერი
                </span>
                <button
                  className="button button-secondary"
                  type="button"
                  disabled={page >= lastPage}
                  onClick={() => setPage(page + 1)}
                >
                  შემდეგი
                </button>
              </div>
            </>
          ) : null}
        </AdminPanel>
      </div>

      {/*
        The detail is its own panel under the list, not a row inside the table. Inside the table
        it inherited the table's 1300px minimum width, so on anything narrower than a desktop the
        action buttons sat off-screen to the right until the operator found the horizontal
        scrollbar.
      */}
      {openId ? (
        <OrderDetail
          key={openId}
          orderId={openId}
          summary={openRow}
          onClose={() => setOpenId(null)}
          onChanged={reload}
        />
      ) : null}
    </AdminScreen>
  );
}

function OrderRow({
  order,
  open,
  onToggle,
}: {
  order: admin.AdminOrderRow;
  open: boolean;
  onToggle: () => void;
}) {
  return (
    <tr
      onClick={onToggle}
      className={open ? "is-open" : ""}
      style={{ cursor: "pointer" }}
      aria-selected={open}
    >
      <td>
        {new Date(order.createdAt).toLocaleDateString("ka-GE")}
        <span className="cell-subtitle">
          {new Date(order.createdAt).toLocaleTimeString("ka-GE", {
            hour: "2-digit",
            minute: "2-digit",
          })}{" "}
          · {order.id.slice(0, 8)}
        </span>
      </td>
      <td>
        {order.customerEmail || order.customerPhone || "—"}
        {order.customerEmail && order.customerPhone ? (
          <span className="cell-subtitle">{order.customerPhone}</span>
        ) : null}
        {order.heroName ? <span className="cell-subtitle">გმირი: {order.heroName}</span> : null}
      </td>
      <td className="book-cell">
        {/* A real button, so the keyboard can open the row the mouse can. */}
        <button type="button" className="row-open" onClick={onToggle} aria-expanded={open}>
          {order.bookTitle || "—"}
        </button>
        <span className="cell-subtitle">
          {order.generationPipeline ? `${label(PIPELINE_TEXT, order.generationPipeline)} · ` : ""}
          {label(BOOK_STATUS_TEXT, order.bookStatus)}
          {typeof order.progressPercent === "number" && isGenerating(order.bookStatus)
            ? ` · ${order.progressPercent}%`
            : ""}
          {pdfSubtitle(order)}
        </span>
        <AttentionChips order={order} />
      </td>
      <td>
        {label(PACKAGE_TEXT, order.package)}
        {order.printStatus ? (
          <span className="cell-subtitle">{label(PRINT_STATUS_TEXT, order.printStatus)}</span>
        ) : null}
      </td>
      <td>
        <span className={statusDot(order.status)} /> {label(ORDER_STATUS_TEXT, order.status)}
        {order.failureReason ? <span className="cell-subtitle">{order.failureReason}</span> : null}
      </td>
      <td>
        {admin.gel(order.totalMinor)}
        {order.discountMinor > 0 ? (
          <span className="cell-subtitle amount-line">−{admin.gel(order.discountMinor)}</span>
        ) : null}
      </td>
    </tr>
  );
}

function isGenerating(status: string | null | undefined): boolean {
  return (
    status === "Pending" ||
    status === "Generating" ||
    status === "GeneratingStory" ||
    status === "GeneratingPdf"
  );
}

function pdfSubtitle(order: admin.AdminOrderRow): string {
  if (order.hasReadingPdf && order.hasPrintPdf) return " · PDF: საკითხავი და საბეჭდი";
  if (order.hasReadingPdf) return " · PDF: საკითხავი";
  if (order.hasPrintPdf) return " · PDF: მხოლოდ საბეჭდი";
  return "";
}

/**
 * What is wrong with this order, visible without opening it.
 *
 * These are the facts SQL can answer for a whole page at once: an unreviewed alarm, a failed
 * book, a book that has gone silent, money with nothing delivered, and a finished book whose file
 * was never published. Which KIND of withhold that last one is takes reading the stored verdict,
 * and that is what opening the row is for.
 */
function AttentionChips({ order }: { order: admin.AdminOrderRow }) {
  const failed = order.bookStatus === "Failed";
  const unfulfilled = order.status === "Paid" && !order.fulfilledAt;
  if (!order.needsAttention && !failed && !order.isStale) return null;

  return (
    <span className="attention-chips">
      {order.openAlarmCount > 0 ? (
        <span className="attention-chip is-alarm" title="განუხილავი შეტყობინებები ამ წიგნზე">
          {order.openAlarmCount} შეტყობინება
        </span>
      ) : null}
      {failed ? (
        <span className="attention-chip is-failed" title="წიგნი ვერ შეიქმნა">
          ვერ შეიქმნა
        </span>
      ) : null}
      {order.isStale ? (
        <span
          className="attention-chip is-failed"
          title={`ბოლო სიგნალი ${admin.ago(order.heartbeatUtc)} წინ — სამუშაო აღარ პასუხობს`}
        >
          გაჩერებული
        </span>
      ) : null}
      {order.withheld ? (
        <span
          className="attention-chip is-review"
          title="წიგნი დასრულებულია, ფაილი კი შეჩერებული — გახსენი, რომ ნახო რატომ"
        >
          შეჩერებული ფაილი
        </span>
      ) : null}
      {unfulfilled ? (
        <span className="attention-chip is-alarm" title="გადახდილია, წიგნი არ მიუღიათ">
          შეუსრულებელი
        </span>
      ) : null}
    </span>
  );
}

function gateLabel(gates: admin.AdminReleaseGates | null): string {
  if (!gates || !gates.verdict) return "შემოწმება არ ჩატარებულა";
  if (gates.awaitingHumanReview) return "ელოდება ვიზუალურ შემოწმებას";
  if (gates.verdict === "RELEASABLE") return "გამოსაშვებად მზადაა";
  return `${gates.failingGates.length} გეითი ვერ გავიდა`;
}

function OrderDetail({
  orderId,
  summary,
  onClose,
  onChanged,
}: {
  orderId: string;
  summary: admin.AdminOrderRow | null;
  onClose: () => void;
  onChanged: () => void;
}) {
  const [detail, setDetail] = useState<admin.AdminOrderDetail | null>(null);
  const [gates, setGates] = useState<admin.AdminReleaseGates | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [action, setAction] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const [reviewNote, setReviewNote] = useState("");
  const [statusNote, setStatusNote] = useState("");
  const [regen, setRegen] = useState<{ scope: admin.RegenerateScope; spread?: number } | null>(
    null,
  );
  const [regenReason, setRegenReason] = useState("");

  /**
   * Quiet on purpose. The first load shows "იტვირთება…"; every later one keeps what is on
   * screen and swaps in the newer answer, so an action's notice survives the refresh it caused.
   */
  const load = useCallback(() => {
    admin
      .getOrder(orderId)
      .then((fresh) => {
        setDetail(fresh);
        setError(null);
      })
      .catch((err: unknown) =>
        setError(err instanceof Error ? err.message : "დეტალები ვერ ჩაიტვირთა."),
      );
    admin
      .getReleaseGates(orderId)
      .then(setGates)
      .catch(() => setGates(null));
  }, [orderId]);

  useEffect(load, [load]);

  // A book being drawn keeps the panel honest without a click: the row is asked again every
  // ten seconds until it stops moving.
  const generating = detail?.book ? isGenerating(detail.book.status) : false;
  useEffect(() => {
    if (!generating) return;
    const timer = window.setInterval(load, 10000);
    return () => window.clearInterval(timer);
  }, [generating, load]);

  const run = async (name: string, work: () => Promise<string>, refreshList = true) => {
    setAction(name);
    setNotice(null);
    try {
      setNotice(await work());
      load();
      if (refreshList) onChanged();
    } catch (err) {
      setNotice(err instanceof Error ? err.message : "ვერ შესრულდა.");
    } finally {
      setAction(null);
    }
  };

  const download = (kind: admin.PdfKind) =>
    run(
      `pdf-${kind}`,
      async () => {
        const { blob, filename } = await admin.downloadOrderPdf(orderId, kind);
        admin.saveBlob(blob, filename ?? `beki-${orderId}-${kind}.pdf`);
        return filename?.includes("READING-COPY")
          ? "ჩამოიტვირთა საკითხავი ასლი — საბეჭდი ფაილი არ არსებობს."
          : "PDF ჩამოიტვირთა.";
      },
      false,
    );

  const downloadPackage = () =>
    run(
      "package",
      async () => {
        const { blob, filename } = await admin.downloadOrderPackage(orderId);
        admin.saveBlob(
          blob,
          filename ??
            (detail?.book ? `BEKI_${detail.book.id}_HANDBACK.zip` : `beki-${orderId}.zip`),
        );
        return "პაკეტი ჩამოიტვირთა.";
      },
      false,
    );

  if (error && !detail) {
    return (
      <div className="panel order-detail-panel">
        <p className="empty-state">{error}</p>
      </div>
    );
  }
  if (!detail) {
    return (
      <div className="panel order-detail-panel">
        <p className="empty-state">იტვირთება…</p>
      </div>
    );
  }

  const { customer, book, shipment, order } = detail;
  const canRetry = detail.canRetry ?? (order.status === "Paid" && !order.fulfilledAt);
  const canRegenerate = detail.canRegenerate === true;
  const isLegacy = book?.generationPipeline === "legacy";
  const openAlarms = (detail.alarms ?? []).filter((a) => !a.reviewedAtUtc);

  return (
    <div className="panel order-detail-panel" id={`order-${orderId}`}>
      <div className="order-detail-head">
        <div>
          <h2>{book?.title || summary?.bookTitle || "შეკვეთა"}</h2>
          <p className="cell-subtitle">
            {order.id} · {label(ORDER_TYPE_TEXT, order.type)} · {label(PACKAGE_TEXT, order.package)}
          </p>
        </div>
        <button type="button" className="button button-secondary" onClick={onClose}>
          დახურვა
        </button>
      </div>

      <div className="order-detail">
        <div className="order-detail-grid">
          <section>
            <h3>დამკვეთი</h3>
            <Field label="სახელი" value={customer.displayName} />
            <Field label="ელფოსტა" value={customer.email} />
            <Field label="ტელეფონი" value={customer.phoneNumber} />
            <Field label="რეგისტრაცია" value={admin.moment(customer.createdAt)} />
            <Field
              label="სულ"
              value={`${customer.orderCount} შეკვეთა · ${customer.bookCount} წიგნი`}
            />
          </section>

          <section>
            <h3>შეკვეთა</h3>
            <Field label="სტატუსი" value={label(ORDER_STATUS_TEXT, order.status)} />
            <Field label="შემოვიდა" value={admin.moment(order.createdAt)} />
            <Field label="გადახდილია" value={admin.moment(order.paidAt)} />
            <Field label="შესრულდა" value={admin.moment(order.fulfilledAt)} />
            <Field
              label="თანხა"
              value={`${admin.gel(order.totalMinor)}${
                order.discountMinor > 0 ? ` (−${admin.gel(order.discountMinor)})` : ""
              }`}
            />
            <Field label="გადახდის სისტემა" value={label(PROVIDER_TEXT, order.provider)} />
            {order.providerPaymentIntentId ? (
              <Field label="ტრანზაქციის ID" value={order.providerPaymentIntentId} />
            ) : null}
            {order.failureReason ? <Field label="შეცდომა" value={order.failureReason} /> : null}
          </section>

          <section>
            <h3>წიგნი</h3>
            {book ? (
              <>
                <Field label="გმირი" value={book.heroName} />
                <Field label="სამყარო" value={label(WORLD_TEXT, book.worldId)} />
                <Field label="კონვეიერი" value={label(PIPELINE_TEXT, book.generationPipeline)} />
                <Field label="სტატუსი" value={label(BOOK_STATUS_TEXT, book.status)} />
                {isGenerating(book.status) || book.progressMessage ? (
                  <Field
                    label="ეტაპი"
                    value={`${book.progressMessage ?? "—"}${
                      typeof book.progressPercent === "number" ? ` · ${book.progressPercent}%` : ""
                    }`}
                  />
                ) : null}
                {book.heartbeatUtc ? (
                  <Field
                    label="ბოლო სიგნალი"
                    value={`${admin.moment(book.heartbeatUtc)} (${admin.ago(book.heartbeatUtc)} წინ)${
                      book.isStale ? " — გაჩერებული" : ""
                    }`}
                  />
                ) : null}
                <Field label="ენა" value={book.storyLanguage} />
                <Field
                  label="ციფრული ნახა"
                  value={book.lastReadAt ? admin.moment(book.lastReadAt) : "ჯერ არ გაუხსნია"}
                />
                <Field
                  label="PDF"
                  value={
                    book.hasReadingPdf || book.hasPrintPdf
                      ? [
                          book.hasReadingPdf ? "საკითხავი" : null,
                          book.hasPrintPdf ? "საბეჭდი" : null,
                        ]
                          .filter(Boolean)
                          .join(" · ")
                      : "ჯერ არ დაგენერირებულა"
                  }
                />
                {order.withheld ? (
                  <Field
                    label="გამოშვება"
                    value={
                      detail.awaitingReview
                        ? "შეჩერებულია — ელოდება ვიზუალურ შემოწმებას"
                        : detail.failingGateCount > 0
                          ? `შეჩერებულია — ${detail.failingGateCount} გეითი ვერ გავიდა`
                          : "შეჩერებულია — მიზეზი ჩანაწერში არ არის"
                    }
                  />
                ) : null}
                {book.errorMessage ? (
                  <p className="detail-field detail-error">
                    <span>შეცდომა{book.failureCode ? ` · ${book.failureCode}` : ""}</span>
                    <strong>{book.errorMessage}</strong>
                  </p>
                ) : null}
              </>
            ) : (
              <p className="cell-subtitle">ამ შეკვეთას წიგნი ჯერ არ აქვს.</p>
            )}
          </section>

          {shipment ? (
            <ShipmentSection
              shipment={shipment}
              onChanged={() => {
                load();
                onChanged();
              }}
            />
          ) : null}
        </div>

        {book ? <BookPictures book={book} /> : null}

        {book && gates ? <GatesTable gates={gates} /> : null}

        <div className="order-detail-actions">
          {book?.hasReadingPdf ? (
            <button
              type="button"
              className="button"
              disabled={action !== null}
              onClick={() => void download("reading")}
            >
              {action === "pdf-reading" ? "იტვირთება…" : "საკითხავი PDF"}
            </button>
          ) : null}
          {book?.hasPrintPdf ? (
            <button
              type="button"
              className="button"
              disabled={action !== null}
              onClick={() => void download("print")}
            >
              {action === "pdf-print" ? "იტვირთება…" : "საბეჭდი PDF"}
            </button>
          ) : null}
          {book ? (
            <button
              type="button"
              className="button button-secondary"
              disabled={action !== null}
              onClick={() => void downloadPackage()}
            >
              {action === "package" ? "იტვირთება…" : "სრული პაკეტი (ZIP)"}
            </button>
          ) : null}

          {book && gates?.verdict ? (
            <span
              className="cell-subtitle"
              title={gates.failingGates.join(", ") || "ყველა გეითი გავიდა."}
            >
              {gateLabel(gates)}
            </span>
          ) : null}

          {/* Legacy books only: a Beki book's PDF is composed by its own job and never by this. */}
          {book && isLegacy && book.status === "StoryReady" && !book.hasReadingPdf ? (
            <button
              type="button"
              className="button button-secondary"
              disabled={action !== null}
              onClick={() =>
                void run("generate", async () => {
                  await admin.generatePdf(book.id);
                  return "PDF-ის მომზადება დაიწყო.";
                })
              }
            >
              {action === "generate" ? "იწყება…" : "PDF-ის დაგენერირება"}
            </button>
          ) : null}

          {canRetry ? (
            <button
              type="button"
              className="button button-secondary"
              disabled={action !== null}
              onClick={() => {
                if (
                  !confirmSpend(
                    "შეკვეთა ხელახლა გაეშვება. ეს წიგნის (ხელახლა) დახატვას ნიშნავს და რეალურ ხარჯს იწვევს. გავაგრძელოთ?",
                  )
                )
                  return;
                void run("retry", async () => {
                  await admin.retryOrder(orderId);
                  return "შეკვეთა ხელახლა გაეშვა.";
                });
              }}
            >
              {action === "retry" ? "მიმდინარეობს…" : "ხელახლა გაშვება"}
            </button>
          ) : null}

          {book && canRegenerate ? (
            <>
              <button
                type="button"
                className="button button-secondary"
                disabled={action !== null}
                onClick={() => setRegen({ scope: "cover" })}
              >
                ყდის ხელახლა დახატვა
              </button>
              <button
                type="button"
                className="button button-secondary"
                disabled={action !== null}
                onClick={() => setRegen({ scope: "book" })}
              >
                მთელი წიგნის ხელახლა დახატვა
              </button>
            </>
          ) : null}

          {order.status === "Paid" || order.status === "Fulfilled" ? (
            <button
              type="button"
              className="button button-secondary"
              disabled={action !== null}
              onClick={() => {
                const note = window.prompt(
                  "შეკვეთა მოინიშნება როგორც დაბრუნებული. თანხის დაბრუნება თავად ბანკის/Stripe-ის პანელიდან ხდება. შენიშვნა (არასავალდებულო):",
                  statusNote,
                );
                if (note === null) return;
                setStatusNote(note);
                void run("refund", async () => {
                  await admin.setOrderStatus(orderId, {
                    status: "Refunded",
                    note: note || undefined,
                  });
                  return "შეკვეთა მოინიშნა დაბრუნებულად.";
                });
              }}
            >
              {action === "refund" ? "…" : "დაბრუნებულად მონიშვნა"}
            </button>
          ) : null}
          {order.status === "Pending" || order.status === "Paid" ? (
            <button
              type="button"
              className="button button-secondary"
              disabled={action !== null}
              onClick={() => {
                const note = window.prompt(
                  "შეკვეთის გაუქმება. შენიშვნა (არასავალდებულო):",
                  statusNote,
                );
                if (note === null) return;
                setStatusNote(note);
                void run("cancel", async () => {
                  await admin.setOrderStatus(orderId, {
                    status: "Cancelled",
                    note: note || undefined,
                  });
                  return "შეკვეთა გაუქმდა.";
                });
              }}
            >
              {action === "cancel" ? "…" : "გაუქმება"}
            </button>
          ) : null}

          {notice ? <span className="cell-subtitle detail-notice">{notice}</span> : null}
        </div>

        {/*
          The human half of VISUAL_QA. It appears only when a person is actually being waited on,
          under the pictures that person is being asked to look at, and it sends the contact-sheet
          hash so approving a stale rendering is refused by the API rather than recorded here.
        */}
        {book && gates?.awaitingHumanReview && gates.contactSheetSha256 ? (
          <div className="review-box">
            <ContactSheet bookId={book.id} />
            <label className="field">
              <span>შენიშვნა ჩანაწერისთვის (არასავალდებულო)</span>
              <input value={reviewNote} onChange={(e) => setReviewNote(e.target.value)} />
            </label>
            <div className="order-detail-actions">
              <button
                type="button"
                className="button"
                disabled={action !== null}
                onClick={() =>
                  void run("approve", async () => {
                    const revised = await admin.approveVisualReview(orderId, {
                      contactSheetSha256: gates.contactSheetSha256!,
                      note: reviewNote.trim() || undefined,
                    });
                    setGates(revised);
                    return revised.verdict === "RELEASABLE"
                      ? "შემოწმება დადასტურდა — წიგნი გამოსაშვებად მზადაა."
                      : "შემოწმება დადასტურდა.";
                  })
                }
              >
                {action === "approve" ? "მიმდინარეობს…" : "დაადასტურე ვიზუალური შემოწმება"}
              </button>
              <span className="cell-subtitle">
                უარყოფა = ხელახლა დახატვა: აირჩიე გვერდი სურათებში ან ყდა ზემოთ.
              </span>
            </div>
          </div>
        ) : null}

        {book && canRegenerate && book.spreadsAvailable && book.spreadsAvailable.length > 0 ? (
          <div className="regen-row">
            <span className="cell-subtitle">ერთი გვერდის ხელახლა დახატვა:</span>
            {book.spreadsAvailable.map((n) => (
              <button
                key={n}
                type="button"
                className="button button-secondary"
                disabled={action !== null}
                onClick={() => setRegen({ scope: "spread", spread: n })}
              >
                გვერდი {n}
              </button>
            ))}
          </div>
        ) : null}

        {regen && book ? (
          <div className="regen-dialog" role="dialog" aria-label="ხელახლა დახატვა">
            <strong>
              {regen.scope === "book"
                ? "მთელი წიგნის ხელახლა დახატვა"
                : regen.scope === "cover"
                  ? "ყდის ხელახლა დახატვა"
                  : `გვერდი ${regen.spread} — ხელახლა დახატვა`}
            </strong>
            <p className="cell-subtitle">
              ეს რეალურ ხარჯს იწვევს ({regen.scope === "book" ? "9 სურათი" : "1–2 სურათი"}) და
              ხელახლა აწყობს PDF-ებს. მშობელს, რომელსაც წიგნი უკვე წაკითხული აქვს, ახალი ვერსია
              მიუვა. ჩაწერე მიზეზი — ის ჩანაწერში დარჩება.
            </p>
            <input
              value={regenReason}
              placeholder="მიზეზი, მაგ. ბავშვის სახე მე-3 გვერდზე სხვაა"
              onChange={(e) => setRegenReason(e.target.value)}
            />
            <div className="order-detail-actions">
              <button
                type="button"
                className="button"
                disabled={action !== null || regenReason.trim().length < 3}
                onClick={() =>
                  void run("regen", async () => {
                    const result = await admin.regenerateBook(book.id, {
                      scope: regen.scope,
                      spread: regen.spread,
                      reason: regenReason.trim(),
                    });
                    setRegen(null);
                    setRegenReason("");
                    return result.message || "ხელახლა დახატვა დაიწყო.";
                  })
                }
              >
                {action === "regen" ? "იწყება…" : "დაადასტურე და დახატე"}
              </button>
              <button
                type="button"
                className="button button-secondary"
                onClick={() => setRegen(null)}
              >
                გაუქმება
              </button>
            </div>
          </div>
        ) : null}

        {detail.alarms && detail.alarms.length > 0 ? (
          <div className="detail-alarms">
            <h3>
              შეტყობინებები ამ წიგნზე{" "}
              <span className="cell-subtitle">
                {openAlarms.length} განუხილავი / {detail.alarms.length} სულ
              </span>
            </h3>
            <ul>
              {detail.alarms.map((alarm) => (
                <AlarmLine
                  key={alarm.id}
                  alarm={alarm}
                  onReviewed={() => {
                    announceAlarmsChanged();
                    load();
                    onChanged();
                  }}
                />
              ))}
            </ul>
          </div>
        ) : null}
      </div>
    </div>
  );
}

function AlarmLine({ alarm, onReviewed }: { alarm: admin.AdminAlarm; onReviewed: () => void }) {
  const [busy, setBusy] = useState(false);
  return (
    <li className={alarm.reviewedAtUtc ? "is-reviewed" : ""}>
      <span className="policy-check">{checkLabel(alarm.checkId)}</span>
      <span className="cell-subtitle">
        {severityText(alarm.severity)} · {admin.moment(alarm.lastSeenUtc)}
        {alarm.reviewedAtUtc
          ? ` · ${RESOLUTION_TEXT[alarm.resolution ?? ""] ?? alarm.resolution ?? "ნანახია"} (${alarm.reviewedBy})`
          : ""}
      </span>
      <span className="alarm-detail">{alarm.detail}</span>
      {!alarm.reviewedAtUtc ? (
        <button
          type="button"
          className="button button-secondary"
          disabled={busy}
          onClick={() => {
            setBusy(true);
            void admin
              .reviewAlarm(alarm.id, "acknowledged")
              .then(onReviewed)
              .finally(() => setBusy(false));
          }}
        >
          {busy ? "…" : "ნანახია"}
        </button>
      ) : null}
    </li>
  );
}

function ShipmentSection({
  shipment,
  onChanged,
}: {
  shipment: admin.AdminOrderShipment;
  onChanged: () => void;
}) {
  const [next, setNext] = useState(shipment.status);
  const [tracking, setTracking] = useState(shipment.trackingCode ?? "");
  const [notify, setNotify] = useState(true);
  const [busy, setBusy] = useState(false);
  const [notice, setNotice] = useState<string | null>(null);

  const save = async () => {
    if (next === "Shipped" && !tracking.trim()) {
      setNotice("გაგზავნისას თვალის მიდევნების კოდი აუცილებელია.");
      return;
    }
    setBusy(true);
    setNotice(null);
    try {
      const updated = await admin.updatePrintOrderStatus(shipment.id, {
        status: next,
        trackingCode: tracking.trim() || undefined,
        notifyCustomer: notify,
      });
      setNotice(
        `${updated.statusLabel}${notify && next !== shipment.status ? " · მშობელს ეცნობა" : ""}`,
      );
      onChanged();
    } catch (err) {
      setNotice(err instanceof Error ? err.message : "ვერ შესრულდა.");
    } finally {
      setBusy(false);
    }
  };

  return (
    <section>
      <h3>მიწოდება</h3>
      <Field
        label="სტატუსი"
        value={shipment.statusLabel ?? label(PRINT_STATUS_TEXT, shipment.status)}
      />
      <Field label="მიმღები" value={shipment.recipientName} />
      <Field label="ტელეფონი" value={shipment.recipientPhone} />
      <Field
        label="მისამართი"
        value={[shipment.city, shipment.addressLine1, shipment.addressLine2]
          .filter(Boolean)
          .join(", ")}
      />
      <Field label="გაიგზავნა" value={admin.moment(shipment.shippedAt)} />
      {shipment.notes ? <Field label="შენიშვნა" value={shipment.notes} /> : null}
      <div className="print-move">
        <select
          value={next}
          onChange={(e) => setNext(e.target.value)}
          aria-label="ამანათის სტატუსი"
        >
          {PRINT_STATUSES.map((s) => (
            <option key={s} value={s}>
              {PRINT_STATUS_TEXT[s]}
            </option>
          ))}
        </select>
        <input
          type="text"
          placeholder="თვალის მიდევნების კოდი"
          value={tracking}
          onChange={(e) => setTracking(e.target.value)}
          aria-label="თვალის მიდევნების კოდი"
        />
        <label className="print-notify">
          <input type="checkbox" checked={notify} onChange={(e) => setNotify(e.target.checked)} />
          მშობელს ეცნობოს
        </label>
        <button
          type="button"
          className="button button-secondary"
          disabled={busy}
          onClick={() => void save()}
        >
          {busy ? "…" : "შენახვა"}
        </button>
        {notice ? <span className="cell-subtitle">{notice}</span> : null}
      </div>
    </section>
  );
}

/**
 * The pictures themselves — the cover and every spread that exists — so the operator who is
 * asked to approve a book has actually seen it. Fetched through the authenticated routes and
 * shown as object URLs, which are revoked when the panel leaves.
 */
function BookPictures({ book }: { book: admin.AdminOrderBook }) {
  const [urls, setUrls] = useState<Record<string, string>>({});
  const [zoom, setZoom] = useState<string | null>(null);
  const spreads = book.spreadsAvailable ?? [];
  const wantCover = book.hasCoverImage !== false;
  const created = useRef<string[]>([]);

  useEffect(() => {
    let cancelled = false;
    const load = async (key: string, path: string) => {
      const url = await admin.fetchImageObjectUrl(path);
      if (!url) return;
      if (cancelled) {
        URL.revokeObjectURL(url);
        return;
      }
      created.current.push(url);
      setUrls((prev) => ({ ...prev, [key]: url }));
    };
    if (wantCover) void load("cover", admin.bookCoverPath(book.id));
    for (const n of spreads) void load(`s${n}`, admin.bookSpreadPath(book.id, n));
    const made = created.current;
    return () => {
      cancelled = true;
      for (const url of made) URL.revokeObjectURL(url);
      made.length = 0;
    };
    // Spread numbers are compared by value; the array identity changes on every poll.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [book.id, wantCover, spreads.join(",")]);

  if (!wantCover && spreads.length === 0) return null;

  const items: { key: string; caption: string }[] = [
    ...(wantCover ? [{ key: "cover", caption: "ყდა" }] : []),
    ...spreads.map((n) => ({ key: `s${n}`, caption: `გვერდი ${n}` })),
  ];

  return (
    <div className="book-strip">
      {items.map((item) => (
        <figure key={item.key}>
          {urls[item.key] ? (
            <button type="button" onClick={() => setZoom(urls[item.key])}>
              <img src={urls[item.key]} alt={item.caption} loading="lazy" />
            </button>
          ) : (
            <span className="book-strip-empty">…</span>
          )}
          <figcaption>{item.caption}</figcaption>
        </figure>
      ))}
      {zoom ? (
        <button
          type="button"
          className="book-zoom"
          onClick={() => setZoom(null)}
          aria-label="დახურვა"
        >
          <img src={zoom} alt="" />
        </button>
      ) : null}
    </div>
  );
}

function ContactSheet({ bookId }: { bookId: string }) {
  const [url, setUrl] = useState<string | null>(null);
  useEffect(() => {
    let current: string | null = null;
    let cancelled = false;
    void admin.fetchImageObjectUrl(admin.bookContactSheetPath(bookId, "digital")).then((u) => {
      if (cancelled) {
        if (u) URL.revokeObjectURL(u);
        return;
      }
      current = u;
      setUrl(u);
    });
    return () => {
      cancelled = true;
      if (current) URL.revokeObjectURL(current);
    };
  }, [bookId]);
  if (!url) return <p className="cell-subtitle">კონტაქტ-ფურცელი იტვირთება…</p>;
  return (
    <figure className="contact-sheet">
      <a href={url} target="_blank" rel="noreferrer">
        <img src={url} alt="კონტაქტ-ფურცელი — ეს არის რენდერი, რომელსაც ხელს აწერ" />
      </a>
      <figcaption>კონტაქტ-ფურცელი: ზუსტად ეს რენდერი დასტურდება.</figcaption>
    </figure>
  );
}

function GatesTable({ gates }: { gates: admin.AdminReleaseGates }) {
  const [open, setOpen] = useState(gates.verdict !== "RELEASABLE");
  if (!gates.verdict) return null;
  return (
    <div className="gates-box">
      <button
        type="button"
        className="gates-toggle"
        onClick={() => setOpen((v) => !v)}
        aria-expanded={open}
      >
        <span className={gates.verdict === "RELEASABLE" ? "dot dot-success" : "dot dot-warning"} />{" "}
        გამოშვების შემოწმება: {gateLabel(gates)}
        <span className="cell-subtitle">
          {" "}
          · {admin.moment(gates.evaluatedAtUtc)} · მშობლის PDF{" "}
          {gates.customerPdfPublished ? "გამოშვებულია" : "შეჩერებულია"} · საბეჭდი{" "}
          {gates.pressFilesPublished ? "გამოშვებულია" : "შეჩერებულია"}
        </span>
      </button>
      {open ? (
        <table className="data-table gates-table">
          <thead>
            <tr>
              <th>შემოწმება</th>
              <th>ფაილი</th>
              <th>შედეგი</th>
              <th>დეტალი</th>
            </tr>
          </thead>
          <tbody>
            {gates.gates.map((gate) => (
              <tr key={`${gate.id}:${gate.class}`}>
                <td>
                  {checkLabel(gate.id)}
                  <span className="cell-subtitle">{gate.id}</span>
                </td>
                <td>{CLASS_TEXT[gate.class] ?? gate.class}</td>
                <td>
                  <span
                    className={
                      gate.status === "PASS"
                        ? "dot dot-success"
                        : gate.status === "FAIL"
                          ? "dot dot-danger"
                          : "dot dot-warning"
                    }
                  />{" "}
                  {GATE_STATUS_TEXT[gate.status] ?? gate.status}
                </td>
                <td className="gate-detail">{gate.detail}</td>
              </tr>
            ))}
          </tbody>
        </table>
      ) : null}
    </div>
  );
}

function Field({ label: name, value }: { label: string; value: string | null | undefined }) {
  return (
    <p className="detail-field">
      <span>{name}</span>
      <strong>{value || "—"}</strong>
    </p>
  );
}
