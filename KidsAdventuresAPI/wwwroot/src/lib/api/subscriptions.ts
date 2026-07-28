/**
 * @deprecated The credit-wallet / Dodo subscription surface is gone. New commerce
 * lives in `@/lib/api/orders`. These stubs keep old marketing components from
 * silently posting to deleted endpoints while the Georgian rebuild replaces them.
 */
import { ApiError } from "./client";
import type {
  AccountBalanceResponse,
  BookPackPlan,
  CheckoutSessionResponse,
  PaymentProvider,
} from "./types";

const RETIRED =
  "კრედიტების საფულე აღარ გამოიყენება. გამოიყენე შეკვეთები (/api/orders).";

export async function getAccountBalance(): Promise<AccountBalanceResponse> {
  throw new ApiError(RETIRED, 410);
}

export async function createCheckoutSession(
  _planType: BookPackPlan,
  _provider?: PaymentProvider,
): Promise<CheckoutSessionResponse> {
  throw new ApiError(RETIRED, 410);
}

export async function confirmCheckoutSession(_options: {
  sessionId?: string;
  paymentId?: string;
  provider?: PaymentProvider;
}): Promise<AccountBalanceResponse> {
  throw new ApiError(RETIRED, 410);
}
