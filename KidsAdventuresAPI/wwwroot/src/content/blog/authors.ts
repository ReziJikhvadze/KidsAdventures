import { BRAND_NAME, BRAND_SOCIAL_LINKS } from "@/lib/brand";

export type Author = {
  id: string;
  name: string;
  /** Short role/title shown under the name, e.g. "Founder, Beki". */
  role: string;
  /** 1–2 sentence factual bio. Keep claims honest — fabricated credentials hurt E-E-A-T. */
  bio: string;
  /** Topics this author writes about — surfaced as Person.knowsAbout for topical authority. */
  knowsAbout?: string[];
  /** Real social/professional profiles for schema sameAs. */
  sameAs?: string[];
};

const TOPICS = [
  "Personalized children's books",
  "Early literacy",
  "Screen-free parenting",
  "Bedtime reading routines",
  "Children's gift ideas",
];

/**
 * Default editorial identity (the brand voice). Used when a post does not name a
 * specific person. A named Person (below) is preferred for E-E-A-T where one genuinely
 * wrote the piece.
 */
export const EDITORIAL_AUTHOR: Author = {
  id: "editorial",
  name: BRAND_NAME,
  role: "Personalized storybooks for kids",
  bio: `${BRAND_NAME} turns a child's name and photo into personalized illustrated adventure books. Our team writes about personalized storytelling, early literacy, and screen-free parenting — drawn from building custom storybooks for families.`,
  knowsAbout: TOPICS,
  sameAs: BRAND_SOCIAL_LINKS.map((link) => link.href),
};

export const AUTHORS: Record<string, Author> = {
  editorial: EDITORIAL_AUTHOR,
  rezi: {
    id: "rezi",
    name: "Rezi Jikhvadze",
    role: `Founder, ${BRAND_NAME}`,
    bio: `Rezi is the founder of ${BRAND_NAME}, where he builds tools that turn a child's name and photo into personalized illustrated adventure books. He writes about personalized storytelling, screen-free parenting, and early literacy.`,
    knowsAbout: TOPICS,
    sameAs: [
      "https://www.linkedin.com/company/adventrya",
      "https://www.pinterest.com/rezijikhvadze/",
      "https://www.tiktok.com/@adventrya.books",
    ],
  },
};

/** The author's canonical on-site profile page (with ProfilePage schema). */
export function authorProfilePath(authorId: string): string {
  return `/blog/author/${authorId}`;
}

export function getAuthor(authorId?: string): Author {
  if (authorId && AUTHORS[authorId]) {
    return AUTHORS[authorId];
  }
  return EDITORIAL_AUTHOR;
}

export function getAuthorById(authorId: string): Author | undefined {
  return AUTHORS[authorId];
}

/** Two-letter initials for the avatar fallback. */
export function authorInitials(name: string): string {
  const parts = name.trim().split(/\s+/).filter(Boolean);
  if (parts.length === 0) return "A";
  if (parts.length === 1) return parts[0].slice(0, 2).toUpperCase();
  return (parts[0][0] + parts[parts.length - 1][0]).toUpperCase();
}
