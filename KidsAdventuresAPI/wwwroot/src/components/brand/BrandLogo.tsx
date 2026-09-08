import { Link } from "@tanstack/react-router";

import { BekiMark } from "@/components/brand/BekiMark";
import { BRAND_NAME } from "@/lib/brand";

type BrandLogoProps = {
  className?: string;
  asLink?: boolean;
  variant?: "default" | "header";
};

/**
 * The brand in the corner of the pages left over from the English site.
 *
 * It used to be two things side by side — the old Adventrya book icon and the name "Beki" set in
 * text — which is one brand's picture next to another brand's name. It is the Beki lockup now,
 * the same image the rest of the site carries, and the word inside it is the only word: the
 * label beside it was repeating what the picture already said.
 *
 * Both surfaces it appears on are dark, so the white finish is the only one it needs.
 */
export function BrandLogo({ className = "", asLink = true, variant = "default" }: BrandLogoProps) {
  const mark = (
    <BekiMark className={variant === "header" ? "brand-logo-mark-header" : "brand-logo-mark"} />
  );

  if (!asLink) {
    return <div className={`flex items-center ${className}`}>{mark}</div>;
  }

  return (
    <Link to="/" className={`flex items-center ${className}`} aria-label={BRAND_NAME}>
      {mark}
    </Link>
  );
}
