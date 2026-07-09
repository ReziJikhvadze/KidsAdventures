import { useState } from "react";
import { BookOpen, Loader2 } from "lucide-react";
import { Link } from "@tanstack/react-router";
import { useBookCheckout } from "@/lib/hooks/useBookCheckout";
import { cn } from "@/lib/utils";

type StoryPathPaywallCardProps = {
  nextThemeLabel: string;
  bookCredits: number;
  className?: string;
};

export function StoryPathPaywallCard({
  nextThemeLabel,
  bookCredits,
  className,
}: StoryPathPaywallCardProps) {
  const { startCheckout, checkingOut } = useBookCheckout();
  const [error, setError] = useState<string | null>(null);

  const handleBuy = async () => {
    setError(null);
    try {
      await startCheckout();
    } catch {
      setError("Could not start checkout. Try again.");
    }
  };

  return (
    <div
      className={cn(
        "rounded-3xl border border-border bg-card p-6 text-center shadow-soft",
        className,
      )}
    >
      <div className="mx-auto mb-3 flex h-12 w-12 items-center justify-center rounded-full bg-primary/10 text-primary">
        <BookOpen className="h-6 w-6" />
      </div>
      <h3 className="font-display text-xl font-semibold">Ready for {nextThemeLabel}?</h3>
      <p className="mt-2 text-sm text-muted-foreground">
        {bookCredits > 0
          ? `You have ${bookCredits} book credit${bookCredits === 1 ? "" : "s"}. Create the next adventure in the generator.`
          : "Unlock another illustrated book for $4.99 when you are ready — no rush."}
      </p>
      <div className="mt-5 flex flex-col items-center gap-3 sm:flex-row sm:justify-center">
        {bookCredits > 0 ? (
          <Link
            to="/"
            hash="generator"
            className="inline-flex min-h-11 items-center justify-center rounded-full bg-primary px-6 py-2.5 text-sm font-semibold text-primary-foreground"
          >
            Create {nextThemeLabel} book
          </Link>
        ) : (
          <button
            type="button"
            onClick={() => void handleBuy()}
            disabled={checkingOut}
            className="inline-flex min-h-11 items-center justify-center gap-2 rounded-full bg-primary px-6 py-2.5 text-sm font-semibold text-primary-foreground disabled:opacity-60"
          >
            {checkingOut && <Loader2 className="h-4 w-4 animate-spin" />}
            Buy a book — $4.99
          </button>
        )}
      </div>
      {error && <p className="mt-3 text-sm text-destructive">{error}</p>}
    </div>
  );
}
