import { useT } from "@/lib/i18n";
import { WORLD_COVER_ART, type WorldId } from "@/lib/worlds";

export function Books() {
  const t = useT();
  return (
    <section className="landing-v3-books landing-v3-section" id="books">
      {/* The h2 went: the books below say what they are, and the eyebrow already names them. */}
      <div className="landing-v3-section-heading">
        <p>{t.landing.books.eyebrow}</p>
        <span>{t.landing.books.lead}</span>
      </div>

      <div className="landing-v3-book-gallery">
        {t.landing.books.examples.map((example, index) => {
          const theme = example.theme as WorldId;
          const image = WORLD_COVER_ART[theme] ?? WORLD_COVER_ART.dinosaurs;
          return (
            <article
              key={example.theme}
              className={`landing-v3-example-book example-book-${index + 1}`}
            >
              {/*
                The cover is the link, and it is the only one.

                It used to be a picture with a "create a similar one" line under it — a second
                thing to read before the obvious thing could be done. A book cover is already a
                door; making it one costs nothing to explain. It opens the map with this world
                already lit, because choosing the world is the step that actually comes next,
                and /themes reads ?world= on arrival.
              */}
              <a
                className="landing-v3-example-art"
                href={`/themes?world=${theme}`}
                aria-label={t.landing.books.exampleAlt(example.title)}
              >
                <img src={image} alt="" />
                <div className="landing-v3-example-overlay" />
                <div className="landing-v3-example-title">
                  <small>{example.meta}</small>
                  <strong>{example.title}</strong>
                </div>
                <span className="landing-v3-book-spine" />
              </a>
            </article>
          );
        })}
      </div>
    </section>
  );
}
