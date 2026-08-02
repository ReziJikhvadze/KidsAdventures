import { Link } from "@tanstack/react-router";
import type { ReactNode } from "react";

import { AppHeader } from "@/components/adventrya/AppHeader";
import { useT } from "@/lib/i18n";

/** Shared Adventrya chrome for privacy / terms / contact (replaces old English site Nav/Footer). */
export function LegalPageShell({ children }: { children: ReactNode }) {
  const t = useT();
  const F = t.common.footer;

  return (
    <div className="screen" style={{ minHeight: "100vh" }}>
      <div className="grain" aria-hidden="true" />
      <AppHeader backHref="/" />
      <main style={{ padding: "24px 0 48px" }}>{children}</main>
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
            <Link to="/dashboard">{F.adventureMap}</Link>
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
        <p className="landing-v3-footer-bottom">{F.madeIn}</p>
      </footer>
    </div>
  );
}
