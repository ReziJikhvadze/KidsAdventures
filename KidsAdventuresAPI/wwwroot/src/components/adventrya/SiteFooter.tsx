import { Link, useLocation } from "@tanstack/react-router";

import { useLogoToTop } from "@/components/adventrya/landing/Header";
import { BekiMark } from "@/components/brand/BekiMark";
import { useT } from "@/lib/i18n";

/**
 * The site's footer — one of them.
 *
 * There were three. The home page had the full set of links; `LegalPageShell` — about, contact,
 * refunds, privacy, terms — carried a shortened copy that had lost "ჩვენ შესახებ", the prices,
 * the FAQ and, most seriously, "მიწოდება და დაბრუნება", the document a card scheme expects to
 * find from every page; and the retired English blog had a third in another language entirely.
 * A reader who reached the contact page could no longer find their way to half the site, and the
 * page they were standing on decided which half.
 *
 * The links live here now and every page gets all of them.
 */
export function SiteFooter() {
  const t = useT();
  const F = t.common.footer;
  const onHome = useLocation().pathname === "/";
  // The footer mark is the same mark, and at the bottom of a long page it is the one a reader is
  // actually next to when they want the top. A no-op anywhere but the home page.
  const toTop = useLogoToTop();

  /*
    A bare fragment on the home page, an absolute one everywhere else.

    `href="#pricing"` scrolls without touching the router, which is right where the section is on
    the page already; on `/about` it would address `/about#pricing`, which is nothing. The whole
    address is what a reader on another page needs, even though following it costs a page load.
  */
  const section = (id: string) => (onHome ? `#${id}` : `/#${id}`);

  return (
    /* Named so the pages it links to can put a reader back where they were standing. The rest
       of the home page's sections carry an id for the same reason; the footer is seven screens
       down, which makes it the one where being returned to the top is most obviously wrong. */
    <footer className="landing-v3-footer" id="footer">
      <div>
        <Link to="/" className="landing-v3-logo" onClick={toTop}>
          <BekiMark />
          <small>{t.common.brandTagline}</small>
        </Link>
        <p>{F.blurb}</p>
      </div>
      <nav>
        <div>
          <strong>{F.product}</strong>
          {/* Named like the link below it: both start in the footer, and the arrow out of the
              picker has to bring a reader back to the foot of a seven-screen page rather than
              the top of it. */}
          <Link to="/themes" search={{ from: "footer" }}>
            {t.common.nav.createBook}
          </Link>
          <a href={section("pricing")}>{t.common.nav.pricing}</a>
          {/* The picker's back arrow is a plain link to a fixed address — it cannot use the
              browser's history the way the pages below it do — so it is told where this reader
              came from. Without it, leaving the picker dropped them seven screens up the page. */}
          <Link to="/themes" search={{ from: "footer" }}>
            {F.chooseWorld}
          </Link>
        </div>
        <div>
          <strong>{F.help}</strong>
          <Link to="/about">{F.about}</Link>
          <Link to="/contact">{F.contact}</Link>
          <a href={section("faq")}>{t.common.nav.faq}</a>
        </div>
        {/*
          Delivery and refunds sits with the legal links rather than under help: it is the
          document a card scheme expects to find from every page, and the footer is the only
          thing on every page.
        */}
        <div>
          <strong>{F.legal}</strong>
          <Link to="/refunds">{F.refunds}</Link>
          <Link to="/privacy">{F.privacy}</Link>
          <Link to="/terms">{F.terms}</Link>
        </div>
      </nav>
    </footer>
  );
}
