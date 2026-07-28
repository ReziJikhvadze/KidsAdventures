import { t } from "@/lib/i18n";
import { WORLD_COVER_ART, type WorldId } from "@/lib/worlds";

import { ArrowIcon } from "./icons";

export function Books() {
  return (
    <section className="landing-v3-books landing-v3-section" id="books">
      <div className="landing-v3-section-heading">
        <p>{t.landing.books.eyebrow}</p>
        <h2>
          {t.landing.books.titleLine1}
          <em>{t.landing.books.titleEm}</em>
          {t.landing.books.titleLine2}
        </h2>
        <span>{t.landing.books.lead}</span>
      </div>

      <div className="landing-v3-book-gallery">
        {t.landing.books.examples.map((example, index) => {
          const theme = example.theme as WorldId;
          const image = WORLD_COVER_ART[theme] ?? WORLD_COVER_ART.dinosaurs;
          return (
            <article key={example.theme} className={`landing-v3-example-book example-book-${index + 1}`}>
              <div className="landing-v3-example-art">
                <img src={image} alt={t.landing.books.exampleAlt(example.title)} />
                <div className="landing-v3-example-overlay" />
                <div className="landing-v3-example-title">
                  <small>{example.meta}</small>
                  <strong>{example.title}</strong>
                </div>
                <span className="landing-v3-book-spine" />
              </div>
              <div className="landing-v3-example-meta">
                <span>{example.age}</span>
                <strong>{t.landing.books.priceFrom}</strong>
                <a href={`/create?world=${theme}#profile`}>
                  {t.landing.books.createSimilar}
                  <ArrowIcon />
                </a>
              </div>
            </article>
          );
        })}
      </div>
    </section>
  );
}
