import { createFileRoute, Link } from "@tanstack/react-router";

import { BlogLayout } from "@/components/blog/BlogLayout";
import { JsonLd } from "@/components/seo/JsonLd";
import { BLOG_POSTS } from "@/content/blog/index";
import { adsenseHeadScripts } from "@/lib/adsense";
import { BRAND_NAME } from "@/lib/brand";
import { buildPageMeta } from "@/lib/seo";
import { buildBreadcrumbSchema, buildWebPageSchema } from "@/lib/structured-data";

export const Route = createFileRoute("/blog/")({
  head: () => {
    const { meta, links } = buildPageMeta({
      title: `Parenting, Child Education & Kids Books Blog — ${BRAND_NAME}`,
      description:
        "Parenting tips, child education through stories, personalized children's books, adventure books for kids, screen-free learning, and printable gift ideas.",
      path: "/blog",
    });
    return { meta, links, headScripts: adsenseHeadScripts };
  },
  component: BlogIndexPage,
});

function BlogIndexPage() {
  return (
    <BlogLayout>
      <JsonLd
        data={[
          buildWebPageSchema({
            path: "/blog",
            title: `Blog — ${BRAND_NAME}`,
            description:
              "Tips for personalized children's storybooks, printable PDF gifts, and screen-free activities.",
          }),
          buildBreadcrumbSchema([
            { name: "Home", path: "/" },
            { name: "Blog", path: "/blog" },
          ]),
        ]}
      />
      <div className="mx-auto max-w-3xl px-6 py-12 md:py-16">
        <Link
          to="/"
          className="text-sm text-muted-foreground hover:text-foreground transition-colors"
        >
          ← Back to home
        </Link>
        <h1 className="mt-6 font-display text-4xl md:text-5xl font-bold text-balance">
          Stories, tips & gift ideas
        </h1>
        <p className="mt-4 text-lg text-muted-foreground text-pretty">
          Practical guides for parents and grandparents using personalized illustrated storybooks.
        </p>

        <ul className="mt-12 space-y-6">
          {BLOG_POSTS.map((post) => (
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
      </div>
    </BlogLayout>
  );
}
