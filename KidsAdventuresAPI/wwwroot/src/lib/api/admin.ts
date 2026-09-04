import { apiRequest, getToken, resolveApiUrl } from "./client";

export type AdminOrderRow = {
  id: string;
  userId: string;
  customerEmail: string | null;
  customerPhone: string | null;
  bookId: string | null;
  bookTitle: string | null;
  type: string;
  package: string;
  status: string;
  currency: string;
  subtotalMinor: number;
  discountMinor: number;
  totalMinor: number;
  failureReason: string | null;
  createdAt: string;
  paidAt: string | null;
  fulfilledAt: string | null;
  /** Which gateway took the money: "Bog", "Stripe", or "Promo" for a free order. */
  provider: string;
  /** The gateway's own reference — BOG's transaction_id, Stripe's payment intent. */
  providerPaymentIntentId: string | null;
  bookStatus: string | null;
  lastReadAt: string | null;
  /** The two files, said separately: a combined flag called a withheld book "ready". */
  hasReadingPdf: boolean;
  hasPrintPdf: boolean;
  /** Unreviewed alarms against this order's book — waived incidents nobody has looked at. */
  openAlarmCount: number;
  /** A finished book whose reading copy was never published. The detail response says why. */
  withheld: boolean;
  /** Alarms, a failed book, money with nothing delivered, or a withheld file. */
  needsAttention: boolean;
  printStatus: string | null;
  printOrderId?: string | null;
  /** Who and where the book is about, for a list that is searched by both. */
  heroName?: string | null;
  worldId?: string | null;
  /** "beki" | "legacy" — which pipeline drew (or is drawing) the book. */
  generationPipeline?: string | null;
  /** Where the job is, for a row that is still being made. */
  progressPercent?: number | null;
  progressMessage?: string | null;
  heartbeatUtc?: string | null;
  /** Generating, and silent for longer than the sweep tolerates. */
  isStale?: boolean;
};

export type AdminOrderCustomer = {
  id: string;
  email: string | null;
  phoneNumber: string | null;
  displayName: string | null;
  preferredLanguage: string | null;
  isAdmin: boolean;
  createdAt: string;
  bookCount: number;
  orderCount: number;
};

export type AdminOrderBook = {
  id: string;
  title: string | null;
  heroName: string | null;
  worldId: string | null;
  status: string;
  sequenceNumber: number;
  storyPageCount: number;
  storyLanguage: string | null;
  coverImageUrl: string | null;
  progressMessage: string | null;
  errorMessage: string | null;
  createdAt: string;
  lastReadAt: string | null;
  hasReadingPdf: boolean;
  hasPrintPdf: boolean;
  generationPipeline?: string | null;
  progressPercent?: number | null;
  heartbeatUtc?: string | null;
  isStale?: boolean;
  primaryCharacterId?: string | null;
  /** Spread numbers (1–8) whose artwork exists in storage. */
  spreadsAvailable?: number[];
  hasCoverImage?: boolean;
  hasContactSheet?: boolean;
  /** The machine code at the front of an error message, when there is one. */
  failureCode?: string | null;
};

export type AdminOrderShipment = {
  id: string;
  status: string;
  statusLabel?: string | null;
  recipientName: string;
  recipientPhone: string;
  city: string;
  region: string | null;
  addressLine1: string;
  addressLine2: string | null;
  postalCode: string | null;
  notes: string | null;
  trackingCode: string | null;
  createdAt: string;
  shippedAt: string | null;
  deliveredAt: string | null;
};

export type AdminOrderDetail = {
  order: AdminOrderRow;
  customer: AdminOrderCustomer;
  book: AdminOrderBook | null;
  shipment: AdminOrderShipment | null;
  /** A person is being waited on: the render is fine and nobody has signed it off yet. */
  awaitingReview: boolean;
  /** How many gates this book fails. Zero beside awaitingReview is the good case. */
  failingGateCount: number;
  /** The server's own answer to "may this order be re-driven" — never duplicated client-side. */
  canRetry?: boolean;
  /** A finished or failed Beki book with no live job: a redraw is possible. */
  canRegenerate?: boolean;
  /** Every alarm ever raised against this book, newest first, reviewed ones included. */
  alarms?: AdminAlarm[];
};

export type AdminCustomerRow = {
  id: string;
  email: string | null;
  phoneNumber: string | null;
  displayName: string | null;
  bookCount: number;
  orderCount: number;
  spendMinor: number;
  isAdmin: boolean;
  createdAt: string;
};

export type Paged<T> = { total: number; page: number; pageSize: number; items: T[] };

/** The saved views. Each is one SQL predicate on the server, so the chip and the filter agree. */
export const PAID_UNFULFILLED = "paid-unfulfilled";
export const NEEDS_ATTENTION = "needs-attention";
export const GENERATING = "generating";
export const STUCK = "stuck";
export const AWAITING_REVIEW = "awaiting-review";
export const FAILED_BOOKS = "failed";

function query(params: Record<string, string | number | boolean | undefined>): string {
  const search = new URLSearchParams();
  for (const [key, value] of Object.entries(params)) {
    if (value !== undefined && value !== "") search.set(key, String(value));
  }
  const q = search.toString();
  return q ? `?${q}` : "";
}

/**
 * A file through the API, as bytes, with the name the server gave it.
 *
 * Not `apiRequest`, which parses JSON. The storage URL is deliberately never handed out — a link
 * that outlives the request is a link that leaks a child's book — so the file arrives here and is
 * handed to the browser from memory. The server's filename matters: it is how "this is the
 * reading copy, not the print file" travels with the download.
 */
async function fetchFile(
  path: string,
  fallbackMessage: string,
): Promise<{ blob: Blob; filename: string | null }> {
  const token = getToken();
  const response = await fetch(resolveApiUrl(path), {
    headers: token ? { Authorization: `Bearer ${token}` } : undefined,
  });

  if (!response.ok) {
    let message = fallbackMessage;
    try {
      const body = (await response.json()) as { message?: string };
      if (body?.message) message = body.message;
    } catch {
      /* a non-JSON error body is still an error; the default message covers it */
    }
    throw new Error(message);
  }

  const disposition = response.headers.get("content-disposition") ?? "";
  const utf8 = /filename\*=UTF-8''([^;]+)/i.exec(disposition);
  const plain = /filename="?([^";]+)"?/i.exec(disposition);
  const filename = utf8 ? decodeURIComponent(utf8[1]) : (plain?.[1] ?? null);

  return { blob: await response.blob(), filename };
}

/** Hands a blob to the browser as a download and revokes the object URL straight away. */
export function saveBlob(blob: Blob, filename: string): void {
  const url = URL.createObjectURL(blob);
  const link = document.createElement("a");
  link.href = url;
  link.download = filename;
  document.body.appendChild(link);
  link.click();
  link.remove();
  URL.revokeObjectURL(url);
}

/**
 * An admin-only image as an object URL for an `<img>`.
 *
 * The image routes want the session token, which an `<img src>` cannot carry, so the bytes are
 * fetched and handed over as a blob URL. The caller revokes it when the picture leaves the screen.
 */
export async function fetchImageObjectUrl(path: string): Promise<string | null> {
  const token = getToken();
  const response = await fetch(resolveApiUrl(path), {
    headers: token ? { Authorization: `Bearer ${token}` } : undefined,
  });
  if (!response.ok) return null;
  return URL.createObjectURL(await response.blob());
}

// -- orders -----------------------------------------------------------------

export function listOrders(params: {
  status?: string;
  search?: string;
  flag?: string;
  page?: number;
  pageSize?: number;
}): Promise<Paged<AdminOrderRow>> {
  return apiRequest<Paged<AdminOrderRow>>(`/api/admin/orders${query(params)}`);
}

export function getOrder(id: string): Promise<AdminOrderDetail> {
  return apiRequest<AdminOrderDetail>(`/api/admin/orders/${id}`);
}

export function retryOrder(id: string): Promise<{ message: string }> {
  return apiRequest<{ message: string }>(`/api/admin/orders/${id}/retry`, { method: "POST" });
}

export function setOrderStatus(
  id: string,
  body: { status: "Refunded" | "Cancelled"; note?: string },
): Promise<{ status: string }> {
  return apiRequest<{ status: string }>(`/api/admin/orders/${id}/status`, {
    method: "POST",
    body: JSON.stringify(body),
  });
}

export function generatePdf(bookId: string): Promise<{ status: string }> {
  return apiRequest<{ status: string }>(`/api/admin/books/${bookId}/generate-pdf`, {
    method: "POST",
  });
}

export type PdfKind = "reading" | "print";

export function downloadOrderPdf(orderId: string, kind?: PdfKind) {
  return fetchFile(
    `/api/admin/orders/${orderId}/pdf${kind ? `?kind=${kind}` : ""}`,
    "PDF ვერ ჩამოიტვირთა.",
  );
}

export function downloadOrderPackage(orderId: string) {
  return fetchFile(`/api/admin/orders/${orderId}/package`, "პაკეტი ვერ ჩამოიტვირთა.");
}

// -- books: pictures and redraws ----------------------------------------------

export function bookSpreadPath(bookId: string, spread: number): string {
  return `/api/admin/books/${bookId}/spreads/${spread}`;
}

export function bookCoverPath(bookId: string): string {
  return `/api/admin/books/${bookId}/cover`;
}

export function bookContactSheetPath(
  bookId: string,
  artifact: "digital" | "press" | "cover",
): string {
  return `/api/admin/books/${bookId}/contact-sheet?artifact=${artifact}`;
}

export type RegenerateScope = "book" | "spread" | "cover";

/**
 * Asks for part or all of a book to be drawn again. Real money: every spread is a paid image
 * call, and the console only reaches this from a dialog that says so and asks for a reason.
 */
export function regenerateBook(
  bookId: string,
  body: { scope: RegenerateScope; spread?: number; reason: string },
): Promise<{ message: string }> {
  return apiRequest<{ message: string }>(`/api/admin/books/${bookId}/regenerate`, {
    method: "POST",
    body: JSON.stringify(body),
  });
}

// -- release gates and review ---------------------------------------------------

/** Rebuild from stored artwork only: no new image-generation charges. */
export function recoverCustomerPdf(bookId: string): Promise<{ message: string }> {
  return apiRequest<{ message: string }>(`/api/admin/books/${bookId}/recover-customer-pdf`, {
    method: "POST",
  });
}

/** One of the sixteen hard gates from BEKI_Acceptance_Gates_v1.json, as the console shows it. */
export type AdminReleaseGate = {
  id: string;
  /** "PASS" | "FAIL" | "NEEDS_HUMAN" | "UNKNOWN" — only the first releases anything. */
  status: string;
  /** "shared" | "press" | "digital" | "package" — which deliverable a failure withholds. */
  class: string;
  detail: string;
};

export type AdminReleaseGates = {
  /** null for a book fulfilled before the gates existed; shown as such rather than hidden. */
  verdict: string | null;
  evaluatedAtUtc: string | null;
  failingGates: string[];
  awaitingHumanReview: boolean;
  /** The rendering a reviewer signs. Sent back with the approval so a stale sheet is refused. */
  contactSheetSha256: string | null;
  customerPdfPublished: boolean;
  pressFilesPublished: boolean;
  gates: AdminReleaseGate[];
};

export function getReleaseGates(orderId: string): Promise<AdminReleaseGates> {
  return apiRequest<AdminReleaseGates>(`/api/admin/orders/${orderId}/release-gates`);
}

export function approveVisualReview(
  orderId: string,
  body: { note?: string; contactSheetSha256: string },
): Promise<AdminReleaseGates> {
  return apiRequest<AdminReleaseGates>(`/api/admin/orders/${orderId}/approve-review`, {
    method: "POST",
    body: JSON.stringify(body),
  });
}

// -- release policy ---------------------------------------------------------------

export type AdminReleaseCheckSetting = {
  checkId: string;
  /** "all" is the wildcard — a check whose severity does not vary by artifact. */
  deliverableClass: string;
  /** "blocker" | "flag". */
  severity: string;
  /** True while nobody has changed it, so the table can say "as shipped". */
  isDefault: boolean;
  updatedBy: string | null;
  updatedAtUtc: string | null;
};

export type AdminReleasePolicy = {
  humanReviewRequired: boolean;
  checks: AdminReleaseCheckSetting[];
};

export type AdminReleasePolicyUpdate = {
  setting: AdminReleaseCheckSetting;
  publishedPacks: number;
  humanReviewRequired: boolean;
};

export function getReleasePolicy(): Promise<AdminReleasePolicy> {
  return apiRequest<AdminReleasePolicy>("/api/admin/release-policy");
}

export function setReleasePolicy(body: {
  checkId: string;
  deliverableClass?: string;
  severity: string;
}): Promise<AdminReleasePolicyUpdate> {
  return apiRequest<AdminReleasePolicyUpdate>("/api/admin/release-policy", {
    method: "PUT",
    body: JSON.stringify(body),
  });
}

// -- alarms ------------------------------------------------------------------------

/** Something the pipeline waived and shipped past rather than died on, waiting for a look. */
export type AdminAlarm = {
  id: string;
  packId: string;
  orderId: string | null;
  userId: string;
  checkId: string;
  severity: string;
  detail: string;
  /** A storage key, not a link. Reached through the evidence route. */
  evidenceBlob: string | null;
  createdAtUtc: string;
  /** Updated rather than duplicated when the same incident recurs. */
  lastSeenUtc: string;
  reviewedBy: string | null;
  reviewedAtUtc: string | null;
  resolution: string | null;
};

/** `openCount` is counted independently of the page, so the header badge says how many exist. */
export type AdminAlarmList = { openCount: number; items: AdminAlarm[] };

export function listAlarms(
  params: { open?: boolean; limit?: number } = {},
): Promise<AdminAlarmList> {
  return apiRequest<AdminAlarmList>(`/api/admin/alarms${query(params)}`);
}

export function alarmEvidencePath(id: string): string {
  return `/api/admin/alarms/${id}/evidence`;
}

export const ALARM_RESOLUTIONS = ["acknowledged", "fixed", "wont_fix", "false_alarm"] as const;

export type AlarmResolution = (typeof ALARM_RESOLUTIONS)[number];

export function reviewAlarm(
  id: string,
  resolution: AlarmResolution,
): Promise<{ id: string; reviewedBy: string }> {
  return apiRequest<{ id: string; reviewedBy: string }>(`/api/admin/alarms/${id}/review`, {
    method: "POST",
    body: JSON.stringify({ resolution }),
  });
}

// -- print orders ------------------------------------------------------------------

export type AdminPrintOrder = {
  id: string;
  orderId: string;
  bookId: string;
  bookTitle: string | null;
  heroName?: string | null;
  bookStatus?: string | null;
  customerEmail: string | null;
  customerPhone: string | null;
  status: string;
  statusLabel: string;
  recipientName: string;
  recipientPhone: string;
  city: string;
  region: string | null;
  addressLine1: string;
  addressLine2: string | null;
  postalCode: string | null;
  notes: string | null;
  trackingCode: string | null;
  hasPrintPdf?: boolean;
  /** The file on offer is the READING copy because no press file exists. */
  pdfIsReadingCopyFallback: boolean;
  totalMinor: number;
  totalFormatted: string;
  createdAt: string;
  shippedAt: string | null;
  deliveredAt: string | null;
};

export type AdminPrintQueue = {
  orders: AdminPrintOrder[];
  /** Count per status, keyed by the status name, for the tab badges. */
  counts: Record<string, number>;
};

export function listPrintQueue(
  params: { status?: string; limit?: number } = {},
): Promise<AdminPrintQueue> {
  return apiRequest<AdminPrintQueue>(`/api/admin/print-orders${query(params)}`);
}

export function updatePrintOrderStatus(
  id: string,
  body: { status: string; trackingCode?: string; notifyCustomer: boolean },
): Promise<AdminPrintOrder> {
  return apiRequest<AdminPrintOrder>(`/api/admin/print-orders/${id}/status`, {
    method: "PUT",
    body: JSON.stringify(body),
  });
}

// -- overview ------------------------------------------------------------------------

export type AdminOverview = {
  paidTodayCount: number;
  revenueTodayMinor: number;
  revenueMonthMinor: number;
  ordersMonthCount: number;
  booksGeneratingCount: number;
  booksStuckCount: number;
  booksFailedCount: number;
  awaitingReviewCount: number;
  openAlarmCount: number;
  printQueue: { awaitingPrint: number; printing: number; shipped: number };
  recentAttention: AdminOrderRow[];
  generatedAtUtc: string;
};

export function getOverview(): Promise<AdminOverview> {
  return apiRequest<AdminOverview>("/api/admin/overview");
}

// -- customers -----------------------------------------------------------------------

export function listCustomers(params: {
  search?: string;
  page?: number;
  pageSize?: number;
}): Promise<Paged<AdminCustomerRow>> {
  return apiRequest<Paged<AdminCustomerRow>>(`/api/admin/customers${query(params)}`);
}

export function setUserAdmin(
  id: string,
  isAdmin: boolean,
): Promise<{ isAdmin: boolean; note: string }> {
  return apiRequest<{ isAdmin: boolean; note: string }>(`/api/admin/users/${id}/admin`, {
    method: "PUT",
    body: JSON.stringify({ isAdmin }),
  });
}

// -- promo codes -----------------------------------------------------------------------

export type AdminPromoCode = {
  id: string;
  code: string;
  discountPercent: number | null;
  isFullDiscount: boolean;
  maxRedemptions: number | null;
  redemptionCount: number;
  oncePerUser: boolean;
  validFromUtc: string | null;
  validUntilUtc: string | null;
  isActive: boolean;
  createdAtUtc: string;
};

export function listPromoCodes(): Promise<AdminPromoCode[]> {
  return apiRequest<AdminPromoCode[]>("/api/admin/promo-codes");
}

export function createPromoCode(body: {
  code: string;
  discountPercent?: number;
  isFullDiscount?: boolean;
  maxRedemptions?: number;
  oncePerUser?: boolean;
  validFromUtc?: string;
  validUntilUtc?: string;
}): Promise<AdminPromoCode> {
  return apiRequest<AdminPromoCode>("/api/admin/promo-codes", {
    method: "POST",
    body: JSON.stringify(body),
  });
}

export function updatePromoCode(
  id: string,
  body: { isActive?: boolean; maxRedemptions?: number | null; validUntilUtc?: string | null },
): Promise<AdminPromoCode> {
  return apiRequest<AdminPromoCode>(`/api/admin/promo-codes/${id}`, {
    method: "PUT",
    body: JSON.stringify(body),
  });
}

// -- hangfire ----------------------------------------------------------------------------

/**
 * Opens the job dashboard. The dashboard is a server-rendered page that cannot carry the session
 * token, so the API first sets a short-lived admin cookie for it; the tab is opened only once
 * that has succeeded.
 */
export async function openHangfire(): Promise<void> {
  await apiRequest<void>("/api/admin/hangfire-session", { method: "POST" });
  window.open(resolveApiUrl("/hangfire"), "_blank", "noopener");
}

// -- formatting ------------------------------------------------------------------------------

/** Minor units to a readable GEL amount. */
export function gel(minor: number): string {
  return `${(minor / 100).toLocaleString("ka-GE", { maximumFractionDigits: 2 })} ₾`;
}

/** A UTC timestamp as a local date and time, or a dash when it never happened. */
export function moment(value: string | null | undefined): string {
  if (!value) return "—";
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return value;
  return `${date.toLocaleDateString("ka-GE")} ${date.toLocaleTimeString("ka-GE", {
    hour: "2-digit",
    minute: "2-digit",
  })}`;
}

/** "3 წთ", "2 სთ", "4 დღე" — how long ago, for a heartbeat or a queue age. */
export function ago(value: string | null | undefined): string {
  if (!value) return "—";
  const ms = Date.now() - new Date(value).getTime();
  if (Number.isNaN(ms)) return "—";
  const minutes = Math.round(ms / 60000);
  if (minutes < 1) return "ახლახან";
  if (minutes < 60) return `${minutes} წთ`;
  const hours = Math.round(minutes / 60);
  if (hours < 48) return `${hours} სთ`;
  return `${Math.round(hours / 24)} დღე`;
}
