import { Link } from "@tanstack/react-router";

import { BRAND_LOGO_URL, BRAND_NAME, BRAND_HEADER_NAME } from "@/lib/brand";

type BrandLogoProps = {
  className?: string;
  asLink?: boolean;
  variant?: "default" | "header";
};

export function BrandLogo({ className = "", asLink = true, variant = "default" }: BrandLogoProps) {
  const isHeader = variant === "header";

  const logo = isHeader ? (
    <>
      {/* Phone + tablet: contained logo (no overflow). ~15% larger on phones for presence. */}
      <img
        src={BRAND_LOGO_URL}
        alt=""
        aria-hidden
        className="h-16 w-16 shrink-0 object-contain sm:h-14 sm:w-14 2xl:hidden"
      />
      {/* Very wide screens: decorative tall logo */}
      <span className="relative hidden 2xl:block h-16 w-[100px] shrink-0">
        <img
          src={BRAND_LOGO_URL}
          alt=""
          aria-hidden
          className="absolute left-0 top-1/2 h-[120px] w-[100px] -translate-y-1/2 object-contain"
        />
      </span>
    </>
  ) : (
    <img
      src={BRAND_LOGO_URL}
      alt=""
      aria-hidden
      className="h-10 w-10 shrink-0 rounded-xl object-contain sm:h-11 sm:w-11"
    />
  );

  const label = isHeader ? (
    <span className="hidden min-[400px]:block font-display text-sm font-bold tracking-tight whitespace-nowrap xl:text-base 2xl:text-lg">
      {BRAND_HEADER_NAME}
    </span>
  ) : (
    <span className="font-display text-lg font-bold tracking-tight sm:text-xl">{BRAND_NAME}</span>
  );

  const layoutClass = isHeader
    ? `flex items-center gap-2 min-w-0 shrink-0 ${className}`
    : `flex items-center gap-2.5 ${className}`;

  const content = (
    <>
      {logo}
      {label}
    </>
  );

  if (!asLink) {
    return <div className={layoutClass}>{content}</div>;
  }

  return (
    <Link to="/" className={layoutClass} aria-label={BRAND_NAME}>
      {content}
    </Link>
  );
}
