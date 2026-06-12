import { apiRequest } from "./client";
import type { AccountBalanceResponse, BookPackPlan, CheckoutSessionResponse } from "./types";

export async function getAccountBalance(): Promise<AccountBalanceResponse> {
  return apiRequest<AccountBalanceResponse>("/api/subscriptions/account");
}

export async function createCheckoutSession(
  planType: BookPackPlan,
): Promise<CheckoutSessionResponse> {
  return apiRequest<CheckoutSessionResponse>("/api/subscriptions/create-checkout-session", {
    method: "POST",
    body: JSON.stringify({ planType }),
  });
}

export async function confirmCheckoutSession(options: {
  sessionId?: string;
  paymentId?: string;
}): Promise<AccountBalanceResponse> {
  return apiRequest<AccountBalanceResponse>("/api/subscriptions/confirm-checkout", {
    method: "POST",
    body: JSON.stringify({
      sessionId: options.sessionId,
      paymentId: options.paymentId,
    }),
  });
}
