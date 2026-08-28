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
  hasPdf: boolean;
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
