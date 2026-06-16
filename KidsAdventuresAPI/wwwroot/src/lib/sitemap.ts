import { BLOG_POSTS } from "@/content/blog/index";
import { STORY_THEMES } from "@/lib/themes";
import { SITE_URL } from "@/lib/seo";

export type SitemapEntry = {
  path: string;
  changefreq: "weekly" | "monthly";
  priority: string;
};

const STATIC_ENTRIES: SitemapEntry[] = [
  { path: "/", changefreq: "weekly", priority: "1.0" },
  { path: "/personalized-childrens-books", changefreq: "monthly", priority: "0.95" },
  { path: "/kids-learning-books", changefreq: "monthly", priority: "0.95" },
  { path: "/themes", changefreq: "monthly", priority: "0.9" },
  { path: "/gift-guide", changefreq: "monthly", priority: "0.9" },
  { path: "/blog", changefreq: "weekly", priority: "0.8" },
  { path: "/contact", changefreq: "monthly", priority: "0.6" },
  { path: "/terms", changefreq: "monthly", priority: "0.3" },
  { path: "/privacy", changefreq: "monthly", priority: "0.3" },
];

export function getSitemapEntries(): SitemapEntry[] {
  const themeEntries: SitemapEntry[] = STORY_THEMES.map((theme) => ({
    path: `/themes/${theme.slug}`,
    changefreq: "monthly" as const,
    priority: "0.8",
  }));

  const blogEntries: SitemapEntry[] = BLOG_POSTS.map((post) => ({
    path: `/blog/${post.slug}`,
    changefreq: "monthly" as const,
    priority: "0.7",
  }));

  return [...STATIC_ENTRIES, ...themeEntries, ...blogEntries];
}

export function buildSitemapXml(lastmod = new Date().toISOString().slice(0, 10)): string {
  const entries = getSitemapEntries();
  const urls = entries
    .map(
      (entry) => `  <url>
    <loc>${SITE_URL}${entry.path === "/" ? "" : entry.path}</loc>
    <lastmod>${lastmod}</lastmod>
    <changefreq>${entry.changefreq}</changefreq>
    <priority>${entry.priority}</priority>
  </url>`,
    )
    .join("\n");

  return `<?xml version="1.0" encoding="UTF-8"?>
<urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">
${urls}
</urlset>
`;
}
