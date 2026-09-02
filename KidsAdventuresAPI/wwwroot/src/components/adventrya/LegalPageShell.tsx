import { Link } from "@tanstack/react-router";
import type { ReactNode } from "react";

import { AppHeader } from "@/components/adventrya/AppHeader";
import { useT } from "@/lib/i18n";

/** Shared Beki chrome for privacy / terms / contact (replaces old English site Nav/Footer). */
export function LegalPageShell({ children }: { children: ReactNode }) {
  const t = useT();
  const F = t.common.footer;

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
      <footer className="landing-v3-footer" style={{ marginTop: "auto" }}>
        <div>
          <Link to="/" className="landing-v3-logo">
            {t.common.brand}
            <small>{t.common.brandTagline}</small>
          </Link>
          <p>{F.blurb}</p>
        </div>
        <nav>
          <div>
            <strong>{F.product}</strong>
            <Link to="/create">{t.common.nav.createBook}</Link>
            <Link to="/world">{F.myWorld}</Link>
          </div>
          <div>
            <strong>{F.help}</strong>
            <Link to="/contact">{F.contact}</Link>
          </div>
          <div>
            <strong>{F.legal}</strong>
            <Link to="/privacy">კონფიდენციალურობა</Link>
            <Link to="/terms">წესები და პირობები</Link>
          </div>
        </nav>
      </footer>
    </div>
  );
}
