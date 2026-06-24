import { createFileRoute, Link, notFound } from "@tanstack/react-router";

import { BlogLayout } from "@/components/blog/BlogLayout";
import { JsonLd } from "@/components/seo/JsonLd";
import {
  authorInitials,
  authorProfilePath,
  getAuthorById,
} from "@/content/blog/authors";
import { getPostsByAuthor } from "@/content/blog/index";
import { BRAND_NAME } from "@/lib/brand";
import { buildPageMeta } from "@/lib/seo";
import { buildBreadcrumbSchema, buildProfilePageSchema } from "@/lib/structured-data";

function socialLabel(url: string): string {
  try {
    const host = new URL(url).hostname.replace(/^www\./, "");
    if (host.includes("linkedin")) return "LinkedIn";
    if (host.includes("pinterest")) return "Pinterest";
    if (host.includes("tiktok")) return "TikTok";
    if (host.includes("instagram")) return "Instagram";
    if (host.includes("youtube")) return "YouTube";
    if (host.includes("facebook")) return "Facebook";
    return host;
  } catch {
    return "Profile";
  }
}

export const Route = createFileRoute("/blog/author/$id")({
  head: ({ params }) => {
    const author = getAuthorById(params.id);
    if (!author) return { meta: [{ title: "Author not found" }] };
    const description = `Articles by ${author.name} — ${author.role}. Guides on personalized children's books, early literacy, and screen-free parenting.`;
    const { meta, links } = buildPageMeta({
      title: `${author.name} — Author at ${BRAND_NAME}`,
      description: description.slice(0, 155),
      path: authorProfilePath(author.id),
      type: "website",
    });
    return { meta, links };
  },
  component: AuthorProfilePage,
});

function AuthorProfilePage() {
  const { id } = Route.useParams();
  const author = getAuthorById(id);
  if (!author) throw notFound();

  const posts = getPostsByAuthor(author.id);

  return (
    <BlogLayout>
      <JsonLd
        data={[
          buildProfilePageSchema({
            path: authorProfilePath(author.id),
            name: author.name,
            description: author.bio,
            knowsAbout: author.knowsAbout,
            sameAs: author.sameAs,
            posts: posts.map((post) => ({ title: post.title, slug: post.slug })),
          }),
          buildBreadcrumbSchema([
            { name: "Home", path: "/" },
            { name: "Blog", path: "/blog" },
            { name: author.name, path: authorProfilePath(author.id) },
          ]),
        ]}
      />

      <div className="mx-auto max-w-3xl px-6 py-12 md:py-16">
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
            <li className="text-foreground font-medium" aria-current="page">
              {author.name}
            </li>
          </ol>
        </nav>

        <header className="mt-8 flex items-start gap-5">
          <div
            className="grid h-16 w-16 shrink-0 place-items-center rounded-full bg-primary/10 text-primary font-display text-xl font-bold"
            aria-hidden
          >
            {authorInitials(author.name)}
          </div>
          <div>
            <h1 className="font-display text-3xl md:text-4xl font-bold text-balance">
              {author.name}
            </h1>
            <p className="mt-1 text-muted-foreground">{author.role}</p>
          </div>
        </header>

        <p className="mt-6 text-lg text-muted-foreground text-pretty leading-relaxed">
          {author.bio}
        </p>

        {author.sameAs && author.sameAs.length > 0 ? (
          <ul className="mt-5 flex flex-wrap gap-2">
            {author.sameAs.map((url) => (
              <li key={url}>
                <a
                  href={url}
                  target="_blank"
                  rel="noopener noreferrer me"
                  className="inline-flex rounded-full border border-border bg-card px-4 py-1.5 text-sm text-foreground hover:border-primary hover:text-primary transition"
                >
                  {socialLabel(url)}
                </a>
              </li>
            ))}
          </ul>
        ) : null}

        {author.knowsAbout && author.knowsAbout.length > 0 ? (
          <div className="mt-8">
            <h2 className="text-xs font-semibold uppercase tracking-wide text-muted-foreground">
              Writes about
            </h2>
            <ul className="mt-3 flex flex-wrap gap-2">
              {author.knowsAbout.map((topic) => (
                <li
                  key={topic}
                  className="rounded-full bg-secondary/60 px-3 py-1 text-sm text-muted-foreground"
                >
                  {topic}
                </li>
              ))}
            </ul>
          </div>
        ) : null}

        <section className="mt-12" aria-labelledby="author-posts-heading">
          <h2
            id="author-posts-heading"
            className="font-display text-2xl font-semibold text-foreground"
          >
            Articles by {author.name}
          </h2>
          {posts.length > 0 ? (
            <ul className="mt-6 space-y-6">
              {posts.map((post) => (
                <li
                  key={post.slug}
                  className="rounded-2xl border border-border bg-card p-6 shadow-soft hover:shadow-card transition"
                >
                  <p className="text-xs text-muted-foreground">
                    {post.publishedAt} · {post.readingTimeMinutes} min read
                  </p>
                  <Link
                    to="/blog/$slug"
                    params={{ slug: post.slug }}
                    className="mt-2 block font-display text-2xl font-semibold hover:text-primary transition"
                  >
                    {post.title}
                  </Link>
                  <p className="mt-2 text-muted-foreground text-pretty">{post.description}</p>
                  <Link
                    to="/blog/$slug"
                    params={{ slug: post.slug }}
                    className="inline-flex mt-4 text-sm font-semibold text-primary hover:underline"
                  >
                    Read article →
                  </Link>
                </li>
              ))}
            </ul>
          ) : (
            <p className="mt-4 text-muted-foreground">No articles yet — check back soon.</p>
          )}
        </section>
      </div>
    </BlogLayout>
  );
}
