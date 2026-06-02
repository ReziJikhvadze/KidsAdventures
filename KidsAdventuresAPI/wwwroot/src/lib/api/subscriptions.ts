import { apiRequest } from "./client";
import type { CheckoutSessionResponse } from "./types";

export async function createCheckoutSession(
  planType = "Premium",
): Promise<CheckoutSessionResponse> {
  return apiRequest<CheckoutSessionResponse>("/api/subscriptions/create-checkout-session", {
    method: "POST",
    body: JSON.stringify({ planType }),
  });
}
