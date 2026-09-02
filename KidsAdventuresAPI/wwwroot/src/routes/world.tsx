import { createFileRoute, redirect } from "@tanstack/react-router";

/**
 * The child's world is part of the parent's space now.
 *
 * It was a screen of its own — the map of six worlds, with the books it was counting on another
 * page entirely. Neither half was complete alone: the shelf could not say where a child was
 * going, and the map could not open, download or print anything it named. `/dashboard` carries
 * both, with the path as its own section.
 *
 * The route stays as a redirect rather than being deleted: it is linked from the footer, from
 * anything a parent has bookmarked, and — the one that would actually break — from the reader,
 * which sends a just-finished book here as `?bookId=` so the world it opened can be celebrated.
 * That query is carried across.
 */
export const Route = createFileRoute("/world")({
  validateSearch: (search: Record<string, unknown>): { bookId?: string } => ({
    bookId: typeof search.bookId === "string" ? search.bookId : undefined,
  }),
  beforeLoad: ({ search }) => {
    throw redirect({
      to: "/dashboard",
      search: search.bookId ? { bookId: search.bookId } : undefined,
      hash: "story-path",
      replace: true,
    });
  },
});
