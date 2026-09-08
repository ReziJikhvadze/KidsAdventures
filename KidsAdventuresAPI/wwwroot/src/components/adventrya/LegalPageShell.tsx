import type { ReactNode } from "react";

import { AppHeader } from "@/components/adventrya/AppHeader";
import { SiteFooter } from "@/components/adventrya/SiteFooter";

/** Shared Beki chrome for privacy / terms / contact (replaces old English site Nav/Footer). */
export function LegalPageShell({ children }: { children: ReactNode }) {
  return (
    /*
      Deliberately not `.screen`.

      That class is the partner demo's fixed tableau: `height: 100svh`, `overflow: hidden`,
      `max-height: 1000px` and `min-width: 1100px`. An inline `min-height` cannot undo any of it,
      so every page wearing this shell — about, contact, refunds, privacy, terms — was clipped at
      one viewport with no way to scroll to the rest, cut off at the bottom, and carrying a
      horizontal scrollbar at any width under 1100px. These are documents; they scroll.
    */
    <div className="legal-page">
      <div className="grain" aria-hidden="true" />
      <AppHeader backHref="/" />
      <main style={{ padding: "20px 0 36px" }}>{children}</main>
      {/* The site's footer, not a shorter copy of it. `.legal-page > main` already takes the
          spare height, so a short document keeps this at the foot of the screen. */}
      <SiteFooter />
    </div>
  );
}
