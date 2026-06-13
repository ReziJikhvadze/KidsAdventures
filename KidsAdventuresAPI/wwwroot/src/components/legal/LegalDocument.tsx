import { Link } from "@tanstack/react-router";

import { LEGAL_LAST_UPDATED } from "@/lib/legal";

export type LegalSection = {
  id: string;
  title: string;
  paragraphs: string[];
  bullets?: string[];
  afterBullets?: string[];
};

type LegalDocumentProps = {
  title: string;
  intro: string;
  sections: LegalSection[];
};

export function LegalDocument({ title, intro, sections }: LegalDocumentProps) {
  return (
    <article className="py-16 md:py-24">
      <div className="mx-auto max-w-3xl px-4 sm:px-6">
        <header className="border-b border-border pb-8">
          <p className="text-sm font-semibold text-primary tracking-wide uppercase">Legal</p>
          <h1 className="mt-3 font-display text-4xl md:text-5xl font-bold text-balance">{title}</h1>
          <p className="mt-4 text-muted-foreground">{intro}</p>
          <p className="mt-3 text-xs text-muted-foreground">Last updated: {LEGAL_LAST_UPDATED}</p>
        </header>

        <div className="mt-10 space-y-10">
          {sections.map((section) => (
            <section key={section.id} id={section.id} className="scroll-mt-24">
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
        <Link key={`${label}-${index}`} to={href} className="text-primary font-semibold hover:underline">
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
