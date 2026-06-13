import { Link } from "@tanstack/react-router";

import type { BlogPost } from "@/content/blog/index";

type BlogArticleProps = {
  post: BlogPost;
};

export function BlogArticle({ post }: BlogArticleProps) {
  return (
    <article className="mx-auto max-w-3xl px-6 py-12 md:py-16">
      <Link
        to="/blog"
        className="text-sm text-muted-foreground hover:text-foreground transition-colors"
      >
        ← All articles
      </Link>
      <header className="mt-6">
        <p className="text-sm text-muted-foreground">
          {post.publishedAt} · {post.readingTimeMinutes} min read
        </p>
        <h1 className="mt-3 font-display text-4xl md:text-5xl font-bold text-balance">
          {post.title}
        </h1>
        <p className="mt-4 text-lg text-muted-foreground text-pretty">{post.intro}</p>
      </header>

      <div className="mt-10 space-y-8 text-muted-foreground leading-relaxed">
        {post.sections.map((section, index) => (
          <section key={index}>
            {section.heading ? (
              <h2 className="font-display text-2xl font-semibold text-foreground">
                {section.heading}
              </h2>
            ) : null}
            <div className={section.heading ? "mt-3 space-y-3" : "space-y-3"}>
              {section.paragraphs.map((paragraph, pIndex) => (
                <p key={pIndex} className="text-pretty">
                  {paragraph}
                </p>
              ))}
            </div>
            {section.bullets && section.bullets.length > 0 ? (
              <ul className="mt-3 list-disc pl-5 space-y-1">
                {section.bullets.map((bullet) => (
                  <li key={bullet}>{bullet}</li>
                ))}
              </ul>
            ) : null}
          </section>
        ))}
      </div>

      <div className="mt-12 rounded-2xl border border-border bg-secondary/40 p-6 text-center">
        <p className="font-display text-xl font-semibold text-foreground">
          Ready to create your child&apos;s story?
        </p>
        <p className="mt-2 text-sm text-muted-foreground">
          Free 2-page preview — no card required.
        </p>
        <Link
          to="/"
          hash="generator"
          className="inline-flex mt-4 items-center rounded-full bg-primary text-primary-foreground px-6 py-2.5 text-sm font-semibold hover:opacity-90 transition"
        >
          Create a free story
        </Link>
      </div>
    </article>
  );
}
