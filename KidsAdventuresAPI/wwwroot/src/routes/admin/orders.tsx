import { createFileRoute } from "@tanstack/react-router";
import { useCallback, useEffect, useState } from "react";

import { AdminPanel, AdminScreen, statusDot, useAdminData } from "@/components/admin/AdminScreen";
import * as admin from "@/lib/api/admin";
import { BRAND_NAME } from "@/lib/brand";
import { buildPageMeta } from "@/lib/seo";

type OrdersSearch = { q?: string };

export const Route = createFileRoute("/admin/orders")({
  // Declared rather than read off window.location, which is where it used to come from. The
  // alert emails deep-link here with ?q={orderId} and so, now, does the alarms list — and a
  // link inside the console has to be a real route link, not a page reload.
  validateSearch: (search: Record<string, unknown>): OrdersSearch => ({
    q: typeof search.q === "string" && search.q.length > 0 ? search.q : undefined,
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

const STATUSES = ["Pending", "Paid", "Fulfilled", "Failed", "Cancelled"];
const PAGE_SIZE = 25;

function OrdersPage() {
  const { q } = Route.useSearch();

  const [search, setSearch] = useState(q ?? "");
  const [status, setStatus] = useState("");
  const [onlyUnfulfilled, setOnlyUnfulfilled] = useState(false);
  // The wider filter, and the one that should normally be on: alarms, failed books, withheld
  // files and money with nothing delivered. Every row it selects is a family waiting on somebody
  // here to notice.
  const [onlyAttention, setOnlyAttention] = useState(false);
  const [page, setPage] = useState(1);
  const [openId, setOpenId] = useState<string | null>(null);

  // A link from the alarms list arrives with a new ?q while this component is already mounted.
  useEffect(() => {
    if (q) {
      setSearch(q);
      setPage(1);
    }
  }, [q]);

  const { data, error, busy, reload } = useAdminData(
    () =>
      admin.listOrders({
        status: status || undefined,
        search: search || undefined,
        flag: onlyAttention
          ? admin.NEEDS_ATTENTION
          : onlyUnfulfilled
            ? admin.PAID_UNFULFILLED
            : undefined,
        page,
        pageSize: PAGE_SIZE,
      }),
    [status, search, onlyUnfulfilled, onlyAttention, page],
  );

  const lastPage = Math.max(1, Math.ceil((data?.total ?? 0) / PAGE_SIZE));

  return (
    <AdminScreen active="orders" title="შეკვეთები" subtitle="ყველა მომხმარებლის შეკვეთა">
      <div className="panel orders-workspace">
        <div className="orders-toolbar">
          <input
            type="search"
            placeholder="ელფოსტა, ტელეფონი, წიგნი ან Order ID..."
            value={search}
            onChange={(e) => {
              setSearch(e.target.value);
              setPage(1);
            }}
            aria-label="ძებნა"
          />
          <select
            value={status}
            onChange={(e) => {
              setStatus(e.target.value);
              setPage(1);
            }}
            aria-label="სტატუსი"
          >
            <option value="">ყველა სტატუსი</option>
            {STATUSES.map((s) => (
              <option key={s} value={s}>
                {s}
              </option>
            ))}
          </select>
          {/*
            Everything with something wrong with it. The narrower filter beside it still exists —
            "paid and nothing delivered" is a specific question people ask — but this is the one
            that answers "is anybody waiting on me", which is what the screen is opened for.
          */}
          <button
            type="button"
            className={`button ${onlyAttention ? "" : "button-secondary"}`}
            aria-pressed={onlyAttention}
            onClick={() => {
              setOnlyAttention((on) => !on);
              setOnlyUnfulfilled(false);
              setPage(1);
            }}
          >
            საჭიროებს ყურადღებას
          </button>
          {/*
            The one row that should ever interrupt someone: paid, and nothing delivered.
            It used to be a count on an overview page, where it could be read and not acted on.
          */}
          <button
            type="button"
            className={`button ${onlyUnfulfilled ? "" : "button-secondary"}`}
            aria-pressed={onlyUnfulfilled}
            onClick={() => {
              setOnlyUnfulfilled((on) => !on);
              setOnlyAttention(false);
              setPage(1);
            }}
          >
            გადახდილი, შეუსრულებელი
          </button>
          {data ? <span className="filter-count">{data.total}</span> : null}
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
                      <OrderRows
                        key={o.id}
                        order={o}
                        open={openId === o.id}
                        onToggle={() => setOpenId(openId === o.id ? null : o.id)}
                        onChanged={reload}
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
    </AdminScreen>
  );
}

/**
 * One order: the summary line, and the panel that opens under it.
 *
 * Two <tr> elements rather than a nested table, so the detail spans the same columns the
 * summary is aligned to and the eye does not have to re-learn the layout on every expand.
 */
function OrderRows({
  order,
  open,
  onToggle,
  onChanged,
}: {
  order: admin.AdminOrderRow;
  open: boolean;
  onToggle: () => void;
  onChanged: () => void;
}) {
  return (
    <>
      <tr onClick={onToggle} style={{ cursor: "pointer" }} aria-expanded={open}>
        <td>
          {new Date(order.createdAt).toLocaleDateString("ka-GE")}
          <span className="cell-subtitle">{order.id.slice(0, 8)}</span>
        </td>
        <td>
          {order.customerEmail || order.customerPhone || "—"}
          {order.customerEmail && order.customerPhone ? (
            <span className="cell-subtitle">{order.customerPhone}</span>
          ) : null}
        </td>
        <td className="book-cell">
          {order.bookTitle || "—"}
          <span className="cell-subtitle">
            {order.lastReadAt ? "წაკითხულია" : "ჯერ არ გაუხსნია"}
            {pdfSubtitle(order)}
          </span>
          <AttentionChips order={order} />
        </td>
        <td>
          {order.package}
          {order.printStatus ? <span className="cell-subtitle">{order.printStatus}</span> : null}
        </td>
        <td>
          <span className={statusDot(order.status)} /> {order.status}
          {order.failureReason ? (
            <span className="cell-subtitle">{order.failureReason}</span>
          ) : null}
        </td>
        <td>
          {admin.gel(order.totalMinor)}
          {order.discountMinor > 0 ? (
            <span className="cell-subtitle amount-line">−{admin.gel(order.discountMinor)}</span>
          ) : null}
        </td>
      </tr>
      {open ? (
        <tr>
          <td colSpan={6}>
            <OrderDetail orderId={order.id} onChanged={onChanged} />
          </td>
        </tr>
      ) : null}
    </>
  );
}

/**
 * Which of the two files exists, said in the row.
 *
 * One combined "PDF მზადაა" stood here, and it was true of a book whose press interior had been
 * written while the reading copy was still withheld — so the row told an operator the file was
 * ready to a customer who could download nothing.
 */
function pdfSubtitle(order: admin.AdminOrderRow): string {
  if (order.hasReadingPdf && order.hasPrintPdf) return " · PDF: საკითხავი და საბეჭდი";
  if (order.hasReadingPdf) return " · PDF: საკითხავი";
  if (order.hasPrintPdf) return " · PDF: მხოლოდ საბეჭდი";
  return "";
}

/**
 * What is wrong with this order, visible without expanding it.
 *
 * The gate chip has always existed and has always been inside the expanded panel, which means
 * finding a book in trouble required opening every row in turn — so nobody did, and withheld
 * books sat unnoticed for as long as it took a parent to complain. These are the three facts
 * SQL can answer for a whole page at once: an unreviewed alarm, a failed book, and a finished
 * book whose file was never published. Which KIND of withhold that last one is takes reading the
 * stored verdict, and that is what opening the row is for.
 */
function AttentionChips({ order }: { order: admin.AdminOrderRow }) {
  const failed = order.bookStatus === "Failed";
  if (!order.needsAttention && !failed) return null;

  const unfulfilled = order.status === "Paid" && !order.fulfilledAt;

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
      {order.withheld ? (
        <span
          className="attention-chip is-review"
          title="წიგნი დასრულებულია, ფაილი კი შეჩერებული — გახსენი მწკრივი, რომ ნახო რატომ"
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

/**
 * The gate chip's one line of text.
 *
 * Three states, because they mean three different things to whoever is looking. RELEASABLE says the
 * files are handed over. "N გეითი ვერ გავიდა" says something measurable is wrong and the package
 * will say which. "ვიზუალური შემოწმება" says nothing is wrong — a person simply has not looked at
 * the rendered book yet, and that is the one state with a button next to it.
 */
function gateLabel(gates: admin.AdminReleaseGates | null): string {
  if (!gates || !gates.verdict) return "შემოწმება არ ჩატარებულა";
  if (gates.awaitingHumanReview) return "ელოდება ვიზუალურ შემოწმებას";
  if (gates.verdict === "RELEASABLE") return "გამოსაშვებად მზადაა";
  return `${gates.failingGates.length} გეითი ვერ გავიდა`;
}

function OrderDetail({ orderId, onChanged }: { orderId: string; onChanged: () => void }) {
  const [detail, setDetail] = useState<admin.AdminOrderDetail | null>(null);
  const [gates, setGates] = useState<admin.AdminReleaseGates | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(true);
  const [action, setAction] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);

  const load = useCallback(() => {
    setBusy(true);
    setError(null);
    admin
      .getOrder(orderId)
      .then(setDetail)
      .catch((err: unknown) =>
        setError(err instanceof Error ? err.message : "დეტალები ვერ ჩაიტვირთა."),
      )
      .finally(() => setBusy(false));

    // Separately and quietly: an order whose book predates the release gates has no verdict, and
    // that is a normal thing to show rather than an error that should replace the whole panel.
    admin
      .getReleaseGates(orderId)
      .then(setGates)
      .catch(() => setGates(null));
  }, [orderId]);

  useEffect(load, [load]);

  const run = async (label: string, work: () => Promise<string>) => {
    setAction(label);
    setNotice(null);
    try {
      setNotice(await work());
      load();
      onChanged();
    } catch (err) {
      setNotice(err instanceof Error ? err.message : "ვერ შესრულდა.");
    } finally {
      setAction(null);
    }
  };

  const downloadPdf = () =>
    run("pdf", async () => {
      const blob = await admin.downloadOrderPdf(orderId);
      // The API deliberately does not hand out the storage URL, so the file arrives as bytes
      // and is handed to the browser from memory. Revoked straight away: the object URL is a
      // live handle on the child's book for as long as it exists.
      const url = URL.createObjectURL(blob);
      const link = document.createElement("a");
      link.href = url;
      link.download = `beki-${orderId}.pdf`;
      link.click();
      URL.revokeObjectURL(url);
      return "PDF ჩამოიტვირთა.";
    });

  const downloadPackage = () =>
    run("package", async () => {
      const blob = await admin.downloadOrderPackage(orderId);
      const url = URL.createObjectURL(blob);
      const link = document.createElement("a");
      link.href = url;
      // The audit's own naming, matching what the API sets on the response, so the file an
      // operator forwards to the supplier is named in the supplier's vocabulary. Keyed by the
      // book rather than the order, which is what the package is about.
      link.download = detail?.book
        ? `BEKI_${detail.book.id}_HANDBACK_v002.zip`
        : `beki-${orderId}-package.zip`;
      link.click();
      URL.revokeObjectURL(url);
      return "პაკეტი ჩამოიტვირთა.";
    });

  if (busy) return <p className="empty-state">იტვირთება…</p>;
  if (error) return <p className="empty-state">{error}</p>;
  if (!detail) return null;

  const { customer, book, shipment, order } = detail;
  const canRetry = order.status === "Paid" && !order.fulfilledAt;

  return (
    <div className="order-detail">
      <div className="order-detail-grid">
        <section>
          <h3>დამკვეთი</h3>
          <Field label="სახელი" value={customer.displayName} />
          <Field label="ელფოსტა" value={customer.email} />
          <Field label="ტელეფონი" value={customer.phoneNumber} />
          <Field label="რეგისტრაცია" value={admin.moment(customer.createdAt)} />
          <Field
            label="სულ შეკვეთა"
            value={`${customer.orderCount} · ${customer.bookCount} წიგნი`}
          />
        </section>

        <section>
          <h3>შეკვეთა</h3>
          <Field label="შემოვიდა" value={admin.moment(order.createdAt)} />
          <Field label="გადახდილია" value={admin.moment(order.paidAt)} />
          <Field label="შესრულდა" value={admin.moment(order.fulfilledAt)} />
          <Field label="ტიპი" value={`${order.type} · ${order.package}`} />
          <Field
            label="თანხა"
            value={`${admin.gel(order.totalMinor)}${
              order.discountMinor > 0 ? ` (−${admin.gel(order.discountMinor)})` : ""
            }`}
          />
          {/*
            The number to quote when asking a bank what happened to somebody's money. It has
            existed on the row since the first BOG payment and lived only in a database column;
            an operator handling a refund had no way to reach it.
          */}
          <Field label="გადახდის სისტემა" value={order.provider} />
          {order.providerPaymentIntentId ? (
            <Field label="ტრანზაქციის ID" value={order.providerPaymentIntentId} />
          ) : null}
          {order.failureReason ? <Field label="შეცდომა" value={order.failureReason} /> : null}
        </section>

        <section>
          <h3>წიგნი</h3>
          {book ? (
            <>
              <Field label="სათაური" value={book.title} />
              <Field label="გმირი" value={book.heroName} />
              <Field label="სამყარო" value={book.worldId} />
              <Field label="სტატუსი" value={book.status} />
              <Field label="ენა" value={book.storyLanguage} />
              <Field
                label="ციფრული ნახა"
                value={book.lastReadAt ? admin.moment(book.lastReadAt) : "ჯერ არ გაუხსნია"}
              />
              <Field
                label="PDF"
                value={
                  book.hasReadingPdf || book.hasPrintPdf
                    ? [book.hasReadingPdf ? "საკითხავი" : null, book.hasPrintPdf ? "საბეჭდი" : null]
                        .filter(Boolean)
                        .join(" · ")
                    : "ჯერ არ დაგენერირებულა"
                }
              />
              {/*
                Why the file is not with the family, in the two words that decide what to do next.
                A pending review has a button under this panel; failing gates have a package to
                open. Read from the order detail rather than the gates request beside it, so the
                answer still appears when the stored verdict cannot be fetched.
              */}
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
              {/*
                The substitution the print queue makes silently. An operator sending this to a
                binder is sending a book whose pages do not divide by four, and the only place
                that was ever said was the download's filename.
              */}
              {order.package === "Print" && !book.hasPrintPdf && book.hasReadingPdf ? (
                <Field
                  label="საბეჭდი ფაილი"
                  value="არ არსებობს — ჩამოიტვირთება საკითხავი ასლი, რომელიც ბეჭდვისთვის არ არის მომზადებული"
                />
              ) : null}
              {book.errorMessage ? <Field label="შეცდომა" value={book.errorMessage} /> : null}
            </>
          ) : (
            <p className="cell-subtitle">ამ შეკვეთას წიგნი ჯერ არ აქვს.</p>
          )}
        </section>

        {shipment ? (
          <section>
            <h3>მიწოდება</h3>
            <Field label="სტატუსი" value={shipment.status} />
            <Field label="მიმღები" value={shipment.recipientName} />
            <Field label="ტელეფონი" value={shipment.recipientPhone} />
            <Field
              label="მისამართი"
              value={[shipment.city, shipment.addressLine1, shipment.addressLine2]
                .filter(Boolean)
                .join(", ")}
            />
            <Field label="თვალის მიდევნება" value={shipment.trackingCode} />
            <Field label="გაიგზავნა" value={admin.moment(shipment.shippedAt)} />
            {shipment.notes ? <Field label="შენიშვნა" value={shipment.notes} /> : null}
          </section>
        ) : null}
      </div>

      <div className="order-detail-actions">
        {book && (book.hasReadingPdf || book.hasPrintPdf) ? (
          <button
            type="button"
            className="button"
            disabled={action !== null}
            onClick={() => void downloadPdf()}
          >
            {action === "pdf" ? "იტვირთება…" : "PDF ჩამოტვირთვა"}
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
            title={
              gates.failingGates.length > 0 ? gates.failingGates.join(", ") : "ყველა გეითი გავიდა."
            }
          >
            {gateLabel(gates)}
          </span>
        ) : null}

        {/*
          The human half of VISUAL_QA. It appears only when a person is actually being waited on —
          a book with a measurable failure needs the failure fixed, not a signature — and it sends
          the contact-sheet hash the chip is describing, so approving a stale rendering is refused
          by the API rather than recorded here.
        */}
        {book && gates?.awaitingHumanReview && gates.contactSheetSha256 ? (
          <button
            type="button"
            className="button button-secondary"
            disabled={action !== null}
            onClick={() =>
              void run("approve", async () => {
                const revised = await admin.approveVisualReview(orderId, {
                  contactSheetSha256: gates.contactSheetSha256!,
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
        ) : null}

        {book && !book.hasReadingPdf && !book.hasPrintPdf ? (
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
            onClick={() =>
              void run("retry", async () => {
                await admin.retryOrder(orderId);
                return "შეკვეთა ხელახლა გაეშვა.";
              })
            }
          >
            {action === "retry" ? "მიმდინარეობს…" : "ხელახლა გაშვება"}
          </button>
        ) : null}

        {notice ? <span className="cell-subtitle">{notice}</span> : null}
      </div>
    </div>
  );
}

function Field({ label, value }: { label: string; value: string | null | undefined }) {
  return (
    <p className="detail-field">
      <span>{label}</span>
      <strong>{value || "—"}</strong>
    </p>
  );
}
