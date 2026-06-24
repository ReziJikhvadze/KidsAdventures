import { BRAND_LOGO_URL, BRAND_NAME, BRAND_SOCIAL_LINKS, BRAND_TAGLINE } from "@/lib/brand";
import { LEGAL_CONTACT_EMAILS } from "@/lib/legal";
import { absoluteUrl, DEFAULT_OG_IMAGE, ORGANIZATION_CONTACT_EMAIL, SITE_URL } from "@/lib/seo";

const DEFAULT_AUTHOR = `${BRAND_NAME} Editorial Team`;

/** Reusable Organization publisher block — Article rich results REQUIRE publisher.logo. */
function publisherNode() {
  return {
    "@type": "Organization",
    name: BRAND_NAME,
    url: SITE_URL,
    logo: {
      "@type": "ImageObject",
      url: absoluteUrl(BRAND_LOGO_URL),
    },
  };
}

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
      "Personalized children's books and illustrated adventure storybooks starring your child. Custom name books, screen-free learning, and printable PDFs for parents and grandparents.",
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
  updatedAt?: string;
  readingTimeMinutes: number;
  intro?: string;
  sections?: { heading?: string; paragraphs: string[]; bullets?: string[] }[];
  author?: { name: string; url?: string; sameAs?: string[] };
  keywords?: string[];
  image?: string;
}) {
  // Flatten the post body so Google sees the full article text, word count, and reading time.
  const bodyParts: string[] = [];
  if (post.intro) bodyParts.push(post.intro);
  for (const section of post.sections ?? []) {
    if (section.heading) bodyParts.push(section.heading);
    bodyParts.push(...section.paragraphs);
    if (section.bullets) bodyParts.push(...section.bullets);
  }
  const articleBody = bodyParts.join("\n\n");
  const wordCount = articleBody ? articleBody.split(/\s+/).filter(Boolean).length : undefined;

  return {
    "@context": "https://schema.org",
    "@type": "BlogPosting",
    headline: post.title,
    description: post.description,
    datePublished: post.publishedAt,
    dateModified: post.updatedAt ?? post.publishedAt,
    image: post.image
      ? post.image.startsWith("http")
        ? post.image
        : absoluteUrl(post.image)
      : DEFAULT_OG_IMAGE,
    author: post.author
      ? {
          "@type": "Person",
          name: post.author.name,
          ...(post.author.url ? { url: post.author.url } : {}),
          ...(post.author.sameAs?.length ? { sameAs: post.author.sameAs } : {}),
        }
      : {
          "@type": "Organization",
          name: DEFAULT_AUTHOR,
          url: SITE_URL,
        },
    publisher: publisherNode(),
    mainEntityOfPage: {
      "@type": "WebPage",
      "@id": absoluteUrl(`/blog/${post.slug}`),
    },
    timeRequired: `PT${post.readingTimeMinutes}M`,
    ...(articleBody ? { articleBody } : {}),
    ...(wordCount ? { wordCount } : {}),
    ...(post.keywords?.length ? { keywords: post.keywords.join(", ") } : {}),
  };
}

export function buildProfilePageSchema(input: {
  path: string;
  name: string;
  description: string;
  /** Person.knowsAbout — topics that establish topical authority. */
  knowsAbout?: string[];
  sameAs?: string[];
  /** Headlines the author has written, surfaced as ListItems. */
  posts?: { title: string; slug: string }[];
}) {
  const profileUrl = absoluteUrl(input.path);
  return {
    "@context": "https://schema.org",
    "@type": "ProfilePage",
    url: profileUrl,
    mainEntity: {
      "@type": "Person",
      name: input.name,
      description: input.description,
      url: profileUrl,
      ...(input.knowsAbout?.length ? { knowsAbout: input.knowsAbout } : {}),
      ...(input.sameAs?.length ? { sameAs: input.sameAs } : {}),
      worksFor: {
        "@type": "Organization",
        name: BRAND_NAME,
        url: SITE_URL,
      },
    },
    ...(input.posts?.length
      ? {
          hasPart: input.posts.map((post) => ({
            "@type": "BlogPosting",
            headline: post.title,
            url: absoluteUrl(`/blog/${post.slug}`),
          })),
        }
      : {}),
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
    // Single fixed price ($4.99). Use Offer.price — `lowPrice` is only valid on AggregateOffer
    // and the value MUST match the on-page price or Google flags the rich result.
    offers: {
      "@type": "Offer",
      url: absoluteUrl(`/themes/${theme.slug}`),
      priceCurrency: "USD",
      price: "4.99",
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
