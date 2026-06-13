import { Link } from "@tanstack/react-router";

import { BRAND_LOGO_URL, BRAND_NAME } from "@/lib/brand";

type BrandLogoProps = {
  className?: string;
  asLink?: boolean;
  /** Header: 120×140 logo centered in the nav bar slot */
  variant?: "default" | "header";
};

export function BrandLogo({
  className = "",
  asLink = true,
  variant = "default",
}: BrandLogoProps) {
  const isHeader = variant === "header";

  const logo = isHeader ? (
    <span className="relative block h-16 w-[120px] shrink-0">
      <img
        src={BRAND_LOGO_URL}
        alt=""
        aria-hidden
        className="absolute left-0 top-1/2 h-[140px] w-[120px] -translate-y-1/2 object-contain"
      />
    </span>
  ) : (
    <img
      src={BRAND_LOGO_URL}
      alt=""
      aria-hidden
      className="h-10 w-10 shrink-0 rounded-xl object-contain sm:h-11 sm:w-11"
    />
  );

  const label = (
    <span
      className={`font-display text-lg font-bold tracking-tight sm:text-xl ${
        isHeader ? "truncate max-w-[9rem] sm:max-w-none" : ""
      }`}
    >
      {BRAND_NAME}
    </span>
  );

  const content = (
    <>
      {logo}
      {label}
    </>
  );

  const layoutClass = isHeader
    ? `flex items-center gap-2 sm:gap-3 min-w-0 ${className}`
    : `flex items-center gap-2.5 ${className}`;

  if (!asLink) {
    return <div className={layoutClass}>{content}</div>;
  }

  return (
    <Link to="/" className={layoutClass}>
      {content}
    </Link>
  );
}
