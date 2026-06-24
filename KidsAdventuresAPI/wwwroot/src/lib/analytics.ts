type PurchaseConversionParams = {
  value?: number;
  currency?: string;
  transactionId?: string;
};

declare global {
  interface Window {
    gtag?: (...args: unknown[]) => void;
  }
}

/** Fires the Google Ads / GA4 purchase conversion configured in Google tag. */
export function trackPurchaseConversion(params: PurchaseConversionParams = {}): void {
  if (typeof window === "undefined" || typeof window.gtag !== "function") return;

  window.gtag("event", "conversion_event_purchase", {
    value: params.value ?? 4.99,
    currency: params.currency ?? "USD",
    transaction_id: params.transactionId,
  });
}
