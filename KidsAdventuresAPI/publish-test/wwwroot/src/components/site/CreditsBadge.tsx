import { Link } from "@tanstack/react-router";
import { Sparkles } from "lucide-react";

import { formatCreditsBadgeLabel } from "@/lib/account/storyQuota";
import { cn } from "@/lib/utils";

type CreditsBadgeProps = {
  credits: number;
  storiesRemainingThisMonth?: number;
  variant?: "compact" | "prominent";
  className?: string;
  linkToPricing?: boolean;
};

export function CreditsBadge({
  credits,
  storiesRemainingThisMonth = 0,
  variant = "compact",
  className,
  linkToPricing = false,
}: CreditsBadgeProps) {
  const label = formatCreditsBadgeLabel({
    bookCredits: credits,
    storiesRemainingThisMonth,
  });

  const inner = (
    <span
      className={cn(
        "inline-flex items-center gap-1.5 font-bold",
        variant === "compact"
          ? "rounded-full border border-amber-300 bg-amber-400 px-2.5 py-1 text-[11px] text-amber-950 shadow-sm"
          : "rounded-2xl border border-amber-300 bg-amber-400 px-4 py-2.5 text-sm text-amber-950 shadow-md",
        className,
      )}
      title="1 free full story per month, plus 1 extra story per purchased credit"
    >
      <Sparkles
        className={cn("shrink-0 text-amber-900", variant === "compact" ? "h-3.5 w-3.5" : "h-5 w-5")}
      />
      <span>{label}</span>
    </span>
  );

  if (linkToPricing && credits === 0 && storiesRemainingThisMonth === 0) {
    return (
      <Link to="/" hash="pricing" className="hover:opacity-90 transition">
        {inner}
      </Link>
    );
  }

  return inner;
}
