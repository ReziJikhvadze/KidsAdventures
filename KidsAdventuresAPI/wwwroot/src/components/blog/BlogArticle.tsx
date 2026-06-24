import { Link } from "@tanstack/react-router";

import type { BlogPost } from "@/content/blog/index";
import { authorInitials, getAuthor } from "@/content/blog/authors";

const AUTHOR_ROUTE = "/blog/author/$id" as const;

type BlogArticleProps = {
  post: BlogPost;
  relatedPosts?: BlogPost[];
};

function slugifyHeading(value: string): string {
  return value
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, "-")
    .replace(/(^-|-$)/g, "");
}

function formatDisplayDate(iso: string): string {
  const date = new Date(iso);
  if (Number.isNaN(date.getTime())) return iso;
  return date.toLocaleDateString("en-US", { year: "numeric", month: "long", day: "numeric" });
}

export function BlogArticle({ post, relatedPosts = [] }: BlogArticleProps) {
  const author = getAuthor(post.authorId);
  const updated = post.updatedAt && post.updatedAt !== post.publishedAt ? post.updatedAt : null;
  const tocSections = post.sections
    .filter((section) => section.heading)
    .map((section) => ({ heading: section.heading as string, id: slugifyHeading(section.heading as string) }));

  return (
    <article className="mx-auto max-w-3xl px-6 py-12 md:py-16">
      {/* Crawlable breadcrumb that mirrors the BreadcrumbList schema. */}
      <nav aria-label="Breadcrumb">
        <ol className="flex flex-wrap items-center gap-1.5 text-sm text-muted-foreground">
          <li>
            <Link to="/" className="hover:text-foreground transition-colors">
              Home
            </Link>
          </li>
          <li aria-hidden>/</li>
          <li>
            <Link to="/blog" className="hover:text-foreground transition-colors">
              Blog
            </Link>
          </li>
          <li aria-hidden>/</li>
          <li className="text-foreground font-medium line-clamp-1" aria-current="page">
            {post.title}
          </li>
        </ol>
      </nav>

      <header className="mt-6">
        <h1 className="font-display text-4xl md:text-5xl font-bold text-balance">{post.title}</h1>
        <p className="mt-4 text-lg text-muted-foreground text-pretty">{post.intro}</p>

        {/* E-E-A-T byline with machine-readable dates. */}
        <div className="mt-5 flex flex-wrap items-center gap-x-3 gap-y-1 text-sm text-muted-foreground">
          <span>
            By{" "}
            <Link
              to={AUTHOR_ROUTE}
              params={{ id: author.id }}
              rel="author"
              className="font-medium text-foreground hover:text-primary transition-colors"
            >
              {author.name}
            </Link>
          </span>
          <span aria-hidden>·</span>
          <time dateTime={post.publishedAt}>{formatDisplayDate(post.publishedAt)}</time>
          {updated ? (
            <>
              <span aria-hidden>·</span>
              <span>
                Updated <time dateTime={updated}>{formatDisplayDate(updated)}</time>
              </span>
            </>
          ) : null}
          <span aria-hidden>·</span>
          <span>{post.readingTimeMinutes} min read</span>
        </div>
      </header>

      {/* Cover image — the LCP element. Preloaded via head(); high priority, fixed ratio = no CLS. */}
      {post.coverImage ? (
        <figure className="mt-8 overflow-hidden rounded-3xl border border-border shadow-card">
          <img
            src={post.coverImage}
            alt={post.coverImageAlt ?? post.title}
            width={1200}
            height={630}
            fetchPriority="high"
            decoding="async"
            className="w-full h-auto object-cover"
          />
        </figure>
      ) : null}

      {/* Table of contents — improves dwell time and can earn SERP jump links. */}
      {tocSections.length > 1 ? (
        <nav aria-label="On this page" className="mt-8 rounded-2xl border border-border bg-secondary/40 p-5">
          <p className="text-sm font-semibold text-foreground">On this page</p>
          <ul className="mt-3 space-y-1.5">
            {tocSections.map((section) => (
              <li key={section.id}>
                <a
                  href={`#${section.id}`}
                  className="text-sm text-muted-foreground hover:text-primary transition-colors"
                >
                  {section.heading}
                </a>
              </li>
            ))}
          </ul>
        </nav>
      ) : null}

      <div className="mt-10 space-y-8 text-muted-foreground leading-relaxed">
        {post.sections.map((section, index) => (
          <section key={index} id={section.heading ? slugifyHeading(section.heading) : undefined}>
            {section.heading ? (
              <h2 className="font-display text-2xl font-semibold text-foreground scroll-mt-24">
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

      {/* Visible FAQ that mirrors the FAQPage schema. */}
      {post.faqs && post.faqs.length > 0 ? (
        <section className="mt-12" aria-labelledby="post-faq-heading">
          <h2
            id="post-faq-heading"
            className="font-display text-2xl font-semibold text-foreground"
          >
            Frequently asked questions
          </h2>
          <dl className="mt-5 space-y-5">
            {post.faqs.map((faq) => (
              <div key={faq.q} className="rounded-2xl border border-border bg-card p-5">
                <dt className="font-display font-semibold text-foreground">{faq.q}</dt>
                <dd className="mt-1.5 text-sm leading-relaxed">{faq.a}</dd>
              </div>
            ))}
          </dl>
        </section>
      ) : null}

      {/* Author bio — on-page E-E-A-T trust signal that complements the Person schema. */}
      <section
        className="mt-12 flex items-start gap-4 rounded-2xl border border-border bg-card p-6"
        aria-label={`About the author, ${author.name}`}
      >
        <div
          className="grid h-12 w-12 shrink-0 place-items-center rounded-full bg-primary/10 text-primary font-display font-bold"
          aria-hidden
        >
          {authorInitials(author.name)}
        </div>
        <div>
          <p className="text-xs font-semibold uppercase tracking-wide text-muted-foreground">
            About the author
          </p>
          <p className="mt-1 font-display font-semibold text-foreground">
            <Link
              to={AUTHOR_ROUTE}
              params={{ id: author.id }}
              className="hover:text-primary transition-colors"
            >
              {author.name}
            </Link>
            <span className="ml-2 text-sm font-normal text-muted-foreground">{author.role}</span>
          </p>
          <p className="mt-1.5 text-sm text-muted-foreground leading-relaxed">{author.bio}</p>
          <Link
            to={AUTHOR_ROUTE}
            params={{ id: author.id }}
            className="mt-2 inline-flex text-sm font-semibold text-primary hover:underline"
          >
            View all articles by {author.name} →
          </Link>
        </div>
      </section>

      <div className="mt-12 rounded-2xl border border-border bg-secondary/40 p-6 text-center">
        <p className="font-display text-xl font-semibold text-foreground">
          Ready to create your child&apos;s story?
        </p>
        <p className="mt-2 text-sm text-muted-foreground">Free preview — no card required.</p>
        <Link
          to="/"
          hash="generator"
          className="inline-flex mt-4 items-center rounded-full bg-primary text-primary-foreground px-6 py-2.5 text-sm font-semibold hover:opacity-90 transition"
        >
          Create a free story
        </Link>
      </div>

      {/* Related articles — keyword-rich internal links that build the topical cluster. */}
      {relatedPosts.length > 0 ? (
        <aside className="mt-12" aria-labelledby="related-posts-heading">
          <h2
            id="related-posts-heading"
            className="font-display text-2xl font-semibold text-foreground"
          >
            Keep reading
          </h2>
          <ul className="mt-5 grid gap-4 sm:grid-cols-2">
            {relatedPosts.map((related) => (
              <li key={related.slug}>
                <Link
                  to="/blog/$slug"
                  params={{ slug: related.slug }}
                  className="block rounded-2xl border border-border bg-card p-5 hover:shadow-soft transition"
                >
                  <span className="font-display font-semibold text-foreground">{related.title}</span>
                  <span className="mt-1.5 block text-sm text-muted-foreground line-clamp-2">
                    {related.description}
                  </span>
                </Link>
              </li>
            ))}
          </ul>
        </aside>
      ) : null}
    </article>
  );
}
