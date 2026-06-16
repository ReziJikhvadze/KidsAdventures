import { BRAND_NAME, BRAND_TAGLINE } from "@/lib/brand";
import { LEGAL_CONTACT_EMAILS, LEGAL_WEBSITE } from "@/lib/legal";

export const SITE_URL = (
  import.meta.env.VITE_SITE_URL?.trim() || LEGAL_WEBSITE
).replace(/\/$/, "");

/** Stable public path — copied to public/og-default.jpg at build time. */
export const DEFAULT_OG_IMAGE_PATH = "/og-default.jpg";

export const DEFAULT_OG_IMAGE = `${SITE_URL}${DEFAULT_OG_IMAGE_PATH}`;

export type PageMetaInput = {
  title: string;
  description: string;
  path?: string;
  image?: string;
  noindex?: boolean;
  type?: "website" | "article";
};

export type PageMetaResult = {
  meta: Array<
    | { title: string }
    | { name: string; content: string }
    | { property: string; content: string }
    | { charSet: string }
  >;
  links: Array<{ rel: string; href: string; type?: string; crossOrigin?: string }>;
};

export function absoluteUrl(path = "/"): string {
  if (path.startsWith("http://") || path.startsWith("https://")) return path;
  const normalized = path.startsWith("/") ? path : `/${path}`;
  return `${SITE_URL}${normalized}`;
}

export function buildPageMeta({
  title,
  description,
  path = "/",
  image = DEFAULT_OG_IMAGE,
  noindex = false,
  type = "website",
}: PageMetaInput): PageMetaResult {
  const canonical = absoluteUrl(path);
  const meta: PageMetaResult["meta"] = [
    { title },
    { name: "description", content: description },
    { property: "og:title", content: title },
    { property: "og:description", content: description },
    { property: "og:type", content: type },
    { property: "og:url", content: canonical },
    { property: "og:site_name", content: BRAND_NAME },
    { property: "og:locale", content: "en_US" },
    { property: "og:image", content: image },
    { name: "twitter:card", content: "summary_large_image" },
    { name: "twitter:title", content: title },
    { name: "twitter:description", content: description },
    { name: "twitter:image", content: image },
  ];

  if (noindex) {
    meta.push({ name: "robots", content: "noindex, nofollow" });
  }

  return {
    meta,
    links: [{ rel: "canonical", href: canonical }],
  };
}

export function buildRootMeta(): PageMetaResult {
  return buildPageMeta({
    title: `Personalized Children's Books & Kids Adventure Stories | ${BRAND_NAME}`,
    description:
      "Create personalized children's books starring your child — illustrated adventure stories for ages 3–12. Child education through reading, screen-free parenting, free preview & printable PDF.",
    path: "/",
  });
}

export const ORGANIZATION_CONTACT_EMAIL = LEGAL_CONTACT_EMAILS[0];
