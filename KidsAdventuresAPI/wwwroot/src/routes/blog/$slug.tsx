import { createFileRoute, Link, notFound } from "@tanstack/react-router";

import { BlogArticle } from "@/components/blog/BlogArticle";
import { BlogLayout } from "@/components/blog/BlogLayout";
import { JsonLd } from "@/components/seo/JsonLd";
import { getBlogPostBySlug } from "@/content/blog/index";
import { buildPageMeta } from "@/lib/seo";
import { buildBlogPostingSchema, buildBreadcrumbSchema } from "@/lib/structured-data";

export const Route = createFileRoute("/blog/$slug")({
  head: ({ params }) => {
    const post = getBlogPostBySlug(params.slug);
    if (!post) return { meta: [{ title: "Article not found" }] };
    const { meta, links } = buildPageMeta({
      title: `${post.title} | Adventrya Books`,
      description: post.description,
      path: `/blog/${post.slug}`,
      type: "article",
    });
    return { meta, links };
  },
  component: BlogPostPage,
});

function BlogPostPage() {
  const { slug } = Route.useParams();
  const post = getBlogPostBySlug(slug);
  if (!post) throw notFound();

  return (
    <BlogLayout>
      <JsonLd
        data={[
          buildBlogPostingSchema(post),
          buildBreadcrumbSchema([
            { name: "Home", path: "/" },
            { name: "Blog", path: "/blog" },
            { name: post.title, path: `/blog/${post.slug}` },
          ]),
        ]}
      />
      <BlogArticle post={post} />
    </BlogLayout>
  );
}
