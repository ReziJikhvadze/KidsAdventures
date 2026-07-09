import { apiRequest } from "./client";
import type {
  AccountBalanceResponse,
  BookPackPlan,
  CheckoutSessionResponse,
  PaymentProvider,
} from "./types";

export async function getAccountBalance(): Promise<AccountBalanceResponse> {
  return apiRequest<AccountBalanceResponse>("/api/subscriptions/account");
}

export async function createCheckoutSession(
  planType: BookPackPlan,
  provider?: PaymentProvider,
  adventurePackId?: string,
): Promise<CheckoutSessionResponse> {
  return apiRequest<CheckoutSessionResponse>("/api/subscriptions/create-checkout-session", {
    method: "POST",
    body: JSON.stringify({ planType, provider, adventurePackId }),
  });
}

export async function confirmCheckoutSession(options: {
  sessionId?: string;
  paymentId?: string;
  provider?: PaymentProvider;
}): Promise<AccountBalanceResponse> {
  return apiRequest<AccountBalanceResponse>("/api/subscriptions/confirm-checkout", {
    method: "POST",
    body: JSON.stringify({
      sessionId: options.sessionId,
      paymentId: options.paymentId,
      provider: options.provider,
    }),
  });
}
