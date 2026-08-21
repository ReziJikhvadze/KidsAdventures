import { apiRequest } from "./client";
import type {
  CheckoutResponse,
  CreateOrderRequest,
  CreatePrintUpgradeOrderRequest,
  OrderStatusResponse,
  QuoteRequest,
  QuoteResponse,
} from "./types";

export async function quoteOrder(request: QuoteRequest): Promise<QuoteResponse> {
  return apiRequest<QuoteResponse>("/api/orders/quote", {
    method: "POST",
    body: JSON.stringify({
      type: request.type ?? "NewBook",
      package: request.package,
      promoCode: request.promoCode || undefined,
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
    if (status.failureReason || status.status === "Failed" || status.status === "Cancelled") {
      throw new Error(status.failureReason ?? "შეკვეთა ვერ შესრულდა.");
    }

    await new Promise((r) => setTimeout(r, intervalMs));
  }

  throw new OrderStillWorkingError();
}
