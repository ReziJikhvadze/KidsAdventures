import { apiRequest } from "./client";
import type {
  AuthChallengeResponse,
  CheckoutResponse,
  CreateOrderRequest,
  CreatePrintUpgradeOrderRequest,
  OrderStatusResponse,
  QuoteRequest,
  QuoteResponse,
  RecipientPhoneStatus,
} from "./types";

export async function quoteOrder(request: QuoteRequest): Promise<QuoteResponse> {
  return apiRequest<QuoteResponse>("/api/orders/quote", {
    method: "POST",
    body: JSON.stringify({
      type: request.type ?? "NewBook",
      package: request.package,
      promoCode: request.promoCode || undefined,
      giftWrap: request.giftWrap ?? false,
      quantity: request.quantity ?? 1,
      deliveryOption: request.deliveryOption,
      city: request.city || undefined,
    }),
  });
}

export async function createOrder(request: CreateOrderRequest): Promise<CheckoutResponse> {
  return apiRequest<CheckoutResponse>("/api/orders", {
    method: "POST",
    body: JSON.stringify(request),
  });
}

export async function createPrintUpgradeOrder(
  request: CreatePrintUpgradeOrderRequest,
): Promise<CheckoutResponse> {
  return apiRequest<CheckoutResponse>("/api/orders/print-upgrade", {
    method: "POST",
    body: JSON.stringify(request),
  });
}

/*
  Proving the handset a parcel is going to.

  A POST for the status read too, because the thing being asked about is a phone number: a GET
  would put it in a query string and from there into an access log and a browser's history.
*/

/** Whether this recipient's number still needs proving, and what to draw if it does. */
export async function getRecipientPhoneStatus(phoneNumber: string): Promise<RecipientPhoneStatus> {
  return apiRequest<RecipientPhoneStatus>("/api/orders/recipient-phone/status", {
    method: "POST",
    body: JSON.stringify({ phoneNumber }),
  });
}

/** Sends four digits to the recipient. */
export async function requestRecipientPhoneCode(
  phoneNumber: string,
): Promise<AuthChallengeResponse> {
  return apiRequest<AuthChallengeResponse>("/api/orders/recipient-phone/code", {
    method: "POST",
    body: JSON.stringify({ phoneNumber }),
  });
}

/** Redeems the code. The number is remembered, so this is asked once and not again. */
export async function verifyRecipientPhone(
  phoneNumber: string,
  code: string,
): Promise<RecipientPhoneStatus> {
  return apiRequest<RecipientPhoneStatus>("/api/orders/recipient-phone/verify", {
    method: "POST",
    body: JSON.stringify({ phoneNumber, code }),
  });
}

export async function getOrderStatus(orderId: string): Promise<OrderStatusResponse> {
  return apiRequest<OrderStatusResponse>(`/api/orders/${orderId}`);
}

export async function confirmOrder(orderId: string): Promise<OrderStatusResponse> {
  return apiRequest<OrderStatusResponse>(`/api/orders/${orderId}/confirm`, {
    method: "POST",
  });
}

/**
 * The poll ran out of patience but nothing went wrong: the book is still being drawn.
 * A separate type because the caller must not paint this as a failure — a Beki book is
 * nine reviewed images and can honestly take longer than any polling window.
 */
export class OrderStillWorkingError extends Error {
  constructor() {
    super("წიგნი ჯერ კიდევ იქმნება.");
    this.name = "OrderStillWorkingError";
  }
}

export class BookFailedError extends Error {
  parentMessage?: string | null;
  constructor(message?: string | null) {
    super(message || "წიგნი ვერ შეიქმნა.");
    this.name = "BookFailedError";
    this.parentMessage = message;
  }
}

export async function pollOrderUntilReady(
  orderId: string,
  onProgress?: (status: OrderStatusResponse) => void,
  options?: { intervalMs?: number; maxAttempts?: number },
): Promise<OrderStatusResponse> {
  const intervalMs = options?.intervalMs ?? 2500;
  const maxAttempts = options?.maxAttempts ?? 180;

  for (let attempt = 0; attempt < maxAttempts; attempt++) {
    const status = await getOrderStatus(orderId);
    onProgress?.(status);

    if (status.bookReady) return status;
    if (status.bookFailed) throw new BookFailedError(status.parentMessage);
    if (status.failureReason || status.status === "Failed" || status.status === "Cancelled") {
      throw new Error(status.failureReason ?? "შეკვეთა ვერ შესრულდა.");
    }

    await new Promise((r) => setTimeout(r, intervalMs));
  }

  throw new OrderStillWorkingError();
}
