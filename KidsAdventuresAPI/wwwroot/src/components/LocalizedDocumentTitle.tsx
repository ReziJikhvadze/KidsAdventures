import { useRouterState } from "@tanstack/react-router";
import { useEffect } from "react";

import { BRAND_NAME } from "@/lib/brand";
import { useLocale, useT } from "@/lib/i18n";

/**
 * Keeps the browser-tab title in the interface language.
 *
 * Route `head:` functions run outside React and cannot read the locale, so the
 * title they emit is always Georgian — which left an English UI sitting under a
 * Georgian tab title. Rather than thread the locale into 28 `buildPageMeta` call
 * sites and force a router invalidation on every language change, the canonical
 * meta is left alone and the visible title is corrected here once the locale is
 * known.
 *
 * Deliberately title-only: `og:*`, `description` and `canonical` stay Georgian,
 * which is the right signal for crawlers on a Georgia-targeted site.
 */
export function LocalizedDocumentTitle() {
  const t = useT();
  const { locale } = useLocale();
  const pathname = useRouterState({ select: (s) => s.location.pathname });

  useEffect(() => {
    if (typeof document === "undefined") return;

    // Longest match first so "/admin/orders" wins over "/admin".
    const key = Object.keys(t.pageMeta)
      .filter((path) => pathname === path || pathname.startsWith(`${path}/`))
      .sort((a, b) => b.length - a.length)[0];

    if (!key) return;
    document.title = `${t.pageMeta[key]} — ${BRAND_NAME}`;
    // `locale` is not read directly, but a change to it must re-run this.
  }, [t, locale, pathname]);

  return null;
}
