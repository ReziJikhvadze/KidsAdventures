import { useCallback, useState } from "react";
import { useAuth } from "@/lib/auth/AuthContext";
import { createCheckoutSession } from "@/lib/api/subscriptions";
import { setPendingIllustration } from "@/lib/account/pendingIllustration";

/**
 * Starts a $4.99 book checkout. When `adventurePackId` is provided, Stripe metadata
 * + sessionStorage remember which book to auto-illustrate after payment.
 */
export function useBookCheckout(adventurePackId?: string | null) {
  const { isAuthenticated } = useAuth();
  const [checkingOut, setCheckingOut] = useState(false);

  const startCheckout = useCallback(async () => {
    if (!isAuthenticated) {
      throw new Error("Sign in required");
    }
    setCheckingOut(true);
    try {
      if (adventurePackId) {
        setPendingIllustration(adventurePackId);
      }
      const session = await createCheckoutSession(
        "Book1",
        "stripe",
        adventurePackId ?? undefined,
      );
      window.location.href = session.checkoutUrl;
    } finally {
      setCheckingOut(false);
    }
  }, [isAuthenticated, adventurePackId]);

  return { startCheckout, checkingOut };
}
