import { BRAND_NAME, BRAND_SOCIAL_LINKS, BRAND_TAGLINE } from "@/lib/brand";
import { LEGAL_CONTACT_EMAILS } from "@/lib/legal";
import { absoluteUrl, ORGANIZATION_CONTACT_EMAIL, SITE_URL } from "@/lib/seo";

export function buildWebSiteSchema() {
  return {
    "@context": "https://schema.org",
    "@type": "WebSite",
    name: BRAND_NAME,
    url: SITE_URL,
    description: BRAND_TAGLINE,
    potentialAction: {
      "@type": "SearchAction",
      target: `${SITE_URL}/#generator`,
      "query-input": "required name=search_term_string",
    },
  };
}

export function buildOrganizationSchema() {
  return {
    "@context": "https://schema.org",
    "@type": "Organization",
    name: BRAND_NAME,
    url: SITE_URL,
    description:
      "Personalized children's books and illustrated adventure storybooks for kids ages 3–12. Custom name books, screen-free learning, and printable PDFs for parents and grandparents.",
    email: ORGANIZATION_CONTACT_EMAIL,
    contactPoint: {
      "@type": "ContactPoint",
      contactType: "customer support",
      email: LEGAL_CONTACT_EMAILS.join(", "),
      availableLanguage: ["English"],
    },
    sameAs: BRAND_SOCIAL_LINKS.map((link) => link.href),
  };
}

export function buildFaqSchema(faqs: { q: string; a: string }[]) {
  return {
    "@context": "https://schema.org",
    "@type": "FAQPage",
    mainEntity: faqs.map((faq) => ({
      "@type": "Question",
      name: faq.q,
      acceptedAnswer: {
        "@type": "Answer",
        text: faq.a,
      },
    })),
  };
}

export function buildBreadcrumbSchema(items: { name: string; path: string }[]) {
  return {
    "@context": "https://schema.org",
    "@type": "BreadcrumbList",
    itemListElement: items.map((item, index) => ({
      "@type": "ListItem",
      position: index + 1,
      name: item.name,
      item: absoluteUrl(item.path),
    })),
  };
}

export function buildBlogPostingSchema(post: {
  slug: string;
  title: string;
  description: string;
  publishedAt: string;
  readingTimeMinutes: number;
}) {
  return {
    "@context": "https://schema.org",
    "@type": "BlogPosting",
    headline: post.title,
    description: post.description,
    datePublished: post.publishedAt,
    author: {
      "@type": "Organization",
      name: BRAND_NAME,
    },
    publisher: {
      "@type": "Organization",
      name: BRAND_NAME,
      url: SITE_URL,
    },
    mainEntityOfPage: absoluteUrl(`/blog/${post.slug}`),
    timeRequired: `PT${post.readingTimeMinutes}M`,
  };
}

export function buildWebPageSchema(input: {
  path: string;
  title: string;
  description: string;
}) {
  return {
    "@context": "https://schema.org",
    "@type": "WebPage",
    name: input.title,
    description: input.description,
    url: absoluteUrl(input.path),
    isPartOf: {
      "@type": "WebSite",
      name: BRAND_NAME,
      url: SITE_URL,
    },
  };
}

export function buildThemeProductSchema(theme: {
  slug: string;
  name: string;
  seoDescription: string;
  image: string;
}) {
  const imageUrl = theme.image.startsWith("http") ? theme.image : absoluteUrl(theme.image);
  return {
    "@context": "https://schema.org",
    "@type": "Product",
    name: `${theme.name} personalized storybook`,
    description: theme.seoDescription,
    image: imageUrl,
    brand: {
      "@type": "Brand",
      name: BRAND_NAME,
    },
    offers: {
      "@type": "Offer",
      url: absoluteUrl(`/themes/${theme.slug}`),
      priceCurrency: "USD",
      lowPrice: "14.99",
      availability: "https://schema.org/InStock",
    },
  };
}

export function buildGiftGuideItemListSchema(
  items: { name: string; description: string }[],
) {
  return {
    "@context": "https://schema.org",
    "@type": "ItemList",
    name: "Personalized storybook gift ideas for kids",
    itemListElement: items.map((item, index) => ({
      "@type": "ListItem",
      position: index + 1,
      name: item.name,
      description: item.description,
    })),
  };
}

export function buildContactPageSchema() {
  return {
    "@context": "https://schema.org",
    "@type": "ContactPage",
    name: `Contact ${BRAND_NAME}`,
    url: absoluteUrl("/contact"),
  };
}
