import type { BlogPost } from "./posts/personalized-dinosaur-gift";
import { personalizedDinosaurGift } from "./posts/personalized-dinosaur-gift";
import { printStorybookAtHome } from "./posts/print-storybook-at-home";
import { screenFreeActivities } from "./posts/screen-free-activities-kids";
import { childEducationThroughStories } from "./posts/child-education-through-stories";
import { parentingBedtimeReading } from "./posts/parenting-bedtime-reading-routine";
import { bestPersonalizedChildrensBooks } from "./posts/best-personalized-childrens-books";

export type { BlogPost, BlogSection } from "./posts/personalized-dinosaur-gift";

export const BLOG_POSTS: BlogPost[] = [
  bestPersonalizedChildrensBooks,
  childEducationThroughStories,
  parentingBedtimeReading,
  personalizedDinosaurGift,
  printStorybookAtHome,
  screenFreeActivities,
];

export function getBlogPostBySlug(slug: string): BlogPost | undefined {
  return BLOG_POSTS.find((post) => post.slug === slug);
}

/** Posts written by an author. Posts with no `authorId` count as the editorial team. */
export function getPostsByAuthor(authorId: string): BlogPost[] {
  return BLOG_POSTS.filter((post) => (post.authorId ?? "editorial") === authorId);
}

/** Author ids that have at least one published post (for routing/sitemaps). */
export function getAuthorIdsWithPosts(): string[] {
  return Array.from(new Set(BLOG_POSTS.map((post) => post.authorId ?? "editorial")));
}
