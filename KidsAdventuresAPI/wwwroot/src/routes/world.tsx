import { createFileRoute } from "@tanstack/react-router";

import { ChildWorldScreen } from "@/components/adventrya/world/ChildWorldScreen";
import { BRAND_NAME } from "@/lib/brand";
import { buildPageMeta } from "@/lib/seo";

type WorldSearch = {
  bookId?: string;
};

export const Route = createFileRoute("/world")({
  // `bookId` is a short-lived handoff from the finished reader. It is never trusted as
  // progress data: ChildWorldScreen only uses it after the map API confirms that this
  // child's completed node belongs to the book.
  validateSearch: (search: Record<string, unknown>): WorldSearch => ({
    bookId: typeof search.bookId === "string" ? search.bookId : undefined,
  }),
  head: () => {
    const { meta, links } = buildPageMeta({
      title: `ბავშვის სამყარო — ${BRAND_NAME}`,
      description:
        "ბავშვის თავგადასავლების ცოცხალი რუკა — ყოველი ახალი წიგნი სამყაროს კიდევ ერთ ნაწილს ხსნის.",
      path: "/world",
      noindex: true,
    });
    return { meta, links };
  },
  component: WorldRoute,
});

function WorldRoute() {
  const { bookId } = Route.useSearch();
  return <ChildWorldScreen celebrationBookId={bookId} />;
}
