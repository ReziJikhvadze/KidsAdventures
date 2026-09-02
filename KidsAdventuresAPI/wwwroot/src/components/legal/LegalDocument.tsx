import { Link } from "@tanstack/react-router";
import { Check } from "lucide-react";

import { LEGAL_LAST_UPDATED } from "@/lib/legal";

/**
 * A numbered part inside one section.
 *
 * A section used to be one run of prose, one list and a closing line, which is all any of these
 * documents needed until the refund guarantee arrived: six cases, each with its own heading and
 * its own list of conditions. Flattening those into a single bullet list would have lost which
 * condition belongs to which promise — the difference between money back and a free reprint.
 */
export type LegalBlock = {
  heading?: string;
  paragraphs?: string[];
  bullets?: string[];
  /** Outcomes rather than conditions: ticked, because each one is something we will do. */
  checks?: string[];
  /** Closing lines under the list. */
  afterBullets?: string[];
};

export type LegalSection = {
  id: string;
  title: string;
  paragraphs: string[];
  bullets?: string[];
  afterBullets?: string[];
  blocks?: LegalBlock[];
  /** Overrides the document's language for one section. See `LegalDocumentProps.lang`. */
  lang?: string;
};

type LegalDocumentProps = {
  title: string;
  intro: string;
  sections: LegalSection[];
  /**
   * What language this document is written in, when it is not the site's.
   *
   * `<html lang="ka">` is the whole app, which is right for three of these four documents and
   * wrong for the two written in English — a Georgian screen-reader voice reading an English
   * privacy policy aloud. Terms is now both at once: English throughout with the refund
   * guarantee in Georgian, so the document says `en` and that one section says `ka`.
   */
  lang?: string;
};

export function LegalDocument({ title, intro, sections, lang }: LegalDocumentProps) {
  return (
    <article className="py-16 md:py-24" lang={lang}>
      <div className="mx-auto max-w-3xl px-4 sm:px-6">
        <header className="border-b border-border pb-8">
          <p className="text-sm font-semibold text-primary tracking-wide uppercase">Legal</p>
          <h1 className="mt-3 font-display text-4xl md:text-5xl font-bold text-balance">{title}</h1>
          <p className="mt-4 text-muted-foreground">{intro}</p>
          <p className="mt-3 text-xs text-muted-foreground">Last updated: {LEGAL_LAST_UPDATED}</p>
        </header>

        <div className="mt-10 space-y-10">
          {sections.map((section) => (
            <section key={section.id} id={section.id} className="scroll-mt-24" lang={section.lang}>
              <h2 className="font-display text-xl md:text-2xl font-semibold">{section.title}</h2>
              <div className="mt-4 space-y-3 text-sm md:text-base text-muted-foreground leading-relaxed">
                {section.paragraphs.map((paragraph) => (
                  <p key={paragraph.slice(0, 48)}>{renderLegalText(paragraph)}</p>
                ))}
                {section.bullets && section.bullets.length > 0 ? (
                  <ul className="list-disc pl-5 space-y-2">
                    {section.bullets.map((item) => (
                      <li key={item.slice(0, 48)}>{renderLegalText(item)}</li>
                    ))}
                  </ul>
                ) : null}
                {section.afterBullets?.map((paragraph) => (
                  <p key={paragraph.slice(0, 48)}>{renderLegalText(paragraph)}</p>
                ))}
              </div>

              {section.blocks && section.blocks.length > 0 ? (
                <div className="mt-6 space-y-5">
                  {section.blocks.map((block, index) => (
                    <div
                      /* Index last, not first: a block may carry only a list, and two of those
                         would otherwise both key on `undefined`. */
                      key={block.heading ?? block.paragraphs?.[0]?.slice(0, 48) ?? index}
                      /* A rule down the left rather than a box: six boxes stacked in one section
                         is heavier than the prose either side of it, and the rule is enough to
                         say "this part belongs to that heading". */
                      className="border-l-2 border-border pl-4 md:pl-5"
                    >
                      {block.heading ? (
                        <h3 className="font-display text-base md:text-lg font-semibold text-foreground">
                          {block.heading}
                        </h3>
                      ) : null}
                      <div className="mt-2 space-y-3 text-sm md:text-base text-muted-foreground leading-relaxed">
                        {block.paragraphs?.map((paragraph) => (
                          <p key={paragraph.slice(0, 48)}>{renderLegalText(paragraph)}</p>
                        ))}
                        {block.bullets && block.bullets.length > 0 ? (
                          <ul className="list-disc pl-5 space-y-2">
                            {block.bullets.map((item) => (
                              <li key={item.slice(0, 48)}>{renderLegalText(item)}</li>
                            ))}
                          </ul>
                        ) : null}
                        {block.checks && block.checks.length > 0 ? (
                          <ul className="space-y-2">
                            {block.checks.map((item) => (
                              <li key={item.slice(0, 48)} className="flex gap-2.5">
                                <Check
                                  className="mt-0.5 h-4 w-4 shrink-0 text-primary"
                                  aria-hidden="true"
                                />
                                <span>{renderLegalText(item)}</span>
                              </li>
                            ))}
                          </ul>
                        ) : null}
                        {block.afterBullets?.map((paragraph) => (
                          <p key={paragraph.slice(0, 48)}>{renderLegalText(paragraph)}</p>
                        ))}
                      </div>
                    </div>
                  ))}
                </div>
              ) : null}
            </section>
          ))}
        </div>

        <p className="mt-12 text-sm text-muted-foreground">
          Questions?{" "}
          <Link to="/contact" className="text-primary font-semibold hover:underline">
            Contact us
          </Link>
          .
        </p>
      </div>
    </article>
  );
}

function renderLegalText(text: string) {
  const parts = text.split(/(\[[^\]]+\]\([^)]+\))/g);
  return parts.map((part, index) => {
    const match = /^\[([^\]]+)\]\(([^)]+)\)$/.exec(part);
    if (!match) return part;
    const [, label, href] = match;
    if (href.startsWith("/")) {
      return (
        <Link
          key={`${label}-${index}`}
          to={href}
          className="text-primary font-semibold hover:underline"
        >
          {label}
        </Link>
      );
    }
    return (
      <a
        key={`${label}-${index}`}
        href={href}
        className="text-primary font-semibold hover:underline"
        target="_blank"
        rel="noopener noreferrer"
      >
        {label}
      </a>
    );
  });
}
