import { createFileRoute, notFound } from "@tanstack/react-router";

import { BlogArticle } from "@/components/blog/BlogArticle";
import { BlogLayout } from "@/components/blog/BlogLayout";
import { JsonLd } from "@/components/seo/JsonLd";
import { BLOG_POSTS, getBlogPostBySlug } from "@/content/blog/index";
import { authorProfilePath, getAuthor } from "@/content/blog/authors";
import { absoluteUrl, buildPageMeta } from "@/lib/seo";
import {
  buildBlogPostingSchema,
  buildBreadcrumbSchema,
  buildFaqSchema,
} from "@/lib/structured-data";

export const Route = createFileRoute("/blog/$slug")({
  head: ({ params }) => {
    const post = getBlogPostBySlug(params.slug);
    if (!post) return { meta: [{ title: "Article not found" }] };
    const { meta, links } = buildPageMeta({
      title: `${post.title} | Adventrya Books`,
      description: post.description,
      path: `/blog/${post.slug}`,
      type: "article",
      // Cover image drives the OG/Twitter card; preload it as the LCP element.
      image: post.coverImage ? absoluteUrl(post.coverImage) : undefined,
      preloadImage: post.coverImage,
    });
    return { meta, links };
  },
  component: BlogPostPage,
});

function BlogPostPage() {
  const { slug } = Route.useParams();
  const post = getBlogPostBySlug(slug);
  if (!post) throw notFound();

  const relatedPosts = BLOG_POSTS.filter((p) => p.slug !== post.slug).slice(0, 3);
  const author = getAuthor(post.authorId);

  const schemas: Record<string, unknown>[] = [
    buildBlogPostingSchema({
      ...post,
      image: post.coverImage,
      author: {
        name: author.name,
        url: absoluteUrl(authorProfilePath(author.id)),
        sameAs: author.sameAs,
      },
    }),
    buildBreadcrumbSchema([
      { name: "Home", path: "/" },
      { name: "Blog", path: "/blog" },
      { name: post.title, path: `/blog/${post.slug}` },
    ]),
  ];
  if (post.faqs?.length) {
    schemas.push(buildFaqSchema(post.faqs));
  }

  return (
    <BlogLayout>
      <JsonLd data={schemas} />
      <BlogArticle post={post} relatedPosts={relatedPosts} />
    </BlogLayout>
  );
}
