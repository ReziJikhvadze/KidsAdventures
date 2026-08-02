import { Link } from "@tanstack/react-router";

import { useT } from "@/lib/i18n";

import { ArrowIcon, BookIcon, SparkleIcon, WorldIcon } from "./icons";

export function How() {
  const t = useT();
  return (
    <section className="landing-v3-how landing-v3-section" id="how">
      <div className="landing-v3-how-heading">
        <p>{t.landing.how.eyebrow}</p>
        <h2>
          {t.landing.how.titleLine1}{" "}
          <em>{t.landing.how.titleEm}</em>
        </h2>
      </div>

      <div className="landing-v3-steps">
        <article>
          <span>01</span>
          <div className="landing-v3-step-visual step-profile">
            <i className="step-avatar">ზ</i>
            <div>
              <b />
              <b />
              <b />
            </div>
            <SparkleIcon />
          </div>
          <h3>{t.landing.how.steps[0].title}</h3>
          <p>{t.landing.how.steps[0].body}</p>
        </article>

        <article>
          <span>02</span>
          <div className="landing-v3-step-visual step-theme">
            <i>
              <WorldIcon type="dinosaurs" />
            </i>
            <i>
              <WorldIcon type="space" />
            </i>
            <i>
              <WorldIcon type="magic" />
            </i>
          </div>
          <h3>{t.landing.how.steps[1].title}</h3>
          <p>{t.landing.how.steps[1].body}</p>
        </article>

        <article>
          <span>03</span>
          <div className="landing-v3-step-visual step-book">
            <BookIcon />
            <i />
            <SparkleIcon />
          </div>
          <h3>{t.landing.how.steps[2].title}</h3>
          <p>{t.landing.how.steps[2].body}</p>
        </article>
      </div>

      <Link className="landing-v3-text-link" to="/create" hash="profile">
        {t.landing.how.cta}
        <ArrowIcon />
      </Link>
    </section>
  );
}
