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
};

export type AdminOrderShipment = {
  id: string;
  status: string;
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

type Paged<T> = { total: number; page: number; pageSize: number; items: T[] };

/** The one saved view: money taken with nothing delivered. */
export const PAID_UNFULFILLED = "paid-unfulfilled";

/** The wider one: alarms, failed books, withheld files and unfulfilled money, together. */
export const NEEDS_ATTENTION = "needs-attention";

function query(params: Record<string, string | number | boolean | undefined>): string {
  const search = new URLSearchParams();
  for (const [key, value] of Object.entries(params)) {
    if (value !== undefined && value !== "") search.set(key, String(value));
  }
  const q = search.toString();
  return q ? `?${q}` : "";
}

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

export function generatePdf(bookId: string): Promise<{ status: string }> {
  return apiRequest<{ status: string }>(`/api/admin/books/${bookId}/generate-pdf`, {
    method: "POST",
  });
}

/**
 * Downloads the book's PDF.
 *
 * Not `apiRequest`, which parses JSON: this response is a file. It goes through fetch with the
 * same auth header and becomes a blob URL, because the API deliberately does not hand out the
 * storage URL — a link that outlives the request is a link that leaks a child's book.
 */
export async function downloadOrderPdf(orderId: string): Promise<Blob> {
  const token = getToken();
  const response = await fetch(resolveApiUrl(`/api/admin/orders/${orderId}/pdf`), {
    headers: token ? { Authorization: `Bearer ${token}` } : undefined,
  });

  if (!response.ok) {
    let message = "PDF ვერ ჩამოიტვირთა.";
    try {
      const body = (await response.json()) as { message?: string };
      if (body?.message) message = body.message;
    } catch {
      /* a non-JSON error body is still an error; the default message covers it */
    }
    throw new Error(message);
  }

  return response.blob();
}

/**
 * Downloads the book's whole handback package as one zip — press files with their preflight
 * reports, the reading copy, the plan, and every spread with its base and composition receipt.
 * Same shape as the PDF download and for the same reason: the file arrives as bytes, never as a
 * storage URL.
 */
export async function downloadOrderPackage(orderId: string): Promise<Blob> {
  const token = getToken();
  const response = await fetch(resolveApiUrl(`/api/admin/orders/${orderId}/package`), {
    headers: token ? { Authorization: `Bearer ${token}` } : undefined,
  });

  if (!response.ok) {
    let message = "პაკეტი ვერ ჩამოიტვირთა.";
    try {
      const body = (await response.json()) as { message?: string };
      if (body?.message) message = body.message;
    } catch {
      /* a non-JSON error body is still an error; the default message covers it */
    }
    throw new Error(message);
  }

  return response.blob();
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

/**
 * Records a reviewer's sign-off on the rendered contact sheet, then re-runs the whole gate
 * evaluation server-side and publishes whatever the new verdict unlocks.
 *
 * `contactSheetSha256` is not optional in practice: the endpoint refuses an approval that names a
 * different rendering than the one on file, which is what stops "somebody once looked at some
 * version of this book" from counting as a resolution.
 */
export function approveVisualReview(
  orderId: string,
  body: { note?: string; contactSheetSha256: string },
): Promise<AdminReleaseGates> {
  return apiRequest<AdminReleaseGates>(`/api/admin/orders/${orderId}/approve-review`, {
    method: "POST",
    body: JSON.stringify(body),
  });
}

/**
 * One check's severity, keyed by check AND deliverable class: the same render validation is a
 * blocker on press files a printer will bill for and a flag on the PDF a parent reads tonight.
 */
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

/** What the change actually did — how many withheld books came out, not merely that it saved. */
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

/** Something the pipeline waived and shipped past rather than died on, waiting for a look. */
export type AdminAlarm = {
  id: string;
  packId: string;
  orderId: string | null;
  userId: string;
  checkId: string;
  severity: string;
  detail: string;
  /** A storage key, not a link. Reached through the admin download routes. */
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

/** The four words an alarm can be closed with — the same four the store's constraint accepts. */
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

/** Minor units to a readable GEL amount. */
export function gel(minor: number): string {
  return `${(minor / 100).toLocaleString("ka-GE", { maximumFractionDigits: 2 })} ₾`;
}

/** A UTC timestamp as a Georgian date and time, or a dash when it never happened. */
export function moment(value: string | null | undefined): string {
  if (!value) return "—";
  const date = new Date(value);
  return `${date.toLocaleDateString("ka-GE")} ${date.toLocaleTimeString("ka-GE", {
    hour: "2-digit",
    minute: "2-digit",
  })}`;
}
