import { createFileRoute } from "@tanstack/react-router";

import { DashboardScreen } from "@/components/adventrya/dashboard/DashboardScreen";
import { BRAND_NAME } from "@/lib/brand";
import { buildPageMeta } from "@/lib/seo";

type DashboardSearch = {
  /**
   * A just-finished book, handed over by the reader through `/world`. It is a hint only: the
   * authenticated map response owns progress, so a guessed id cannot select an unrelated world.
   */
  bookId?: string;
};

export const Route = createFileRoute("/dashboard")({
  validateSearch: (search: Record<string, unknown>): DashboardSearch => ({
    bookId: typeof search.bookId === "string" ? search.bookId : undefined,
  }),
  head: () => {
    const { meta, links } = buildPageMeta({
      title: `მშობლის სივრცე — ${BRAND_NAME}`,
      description: "ბავშვების პროფილები, წიგნების ბიბლიოთეკა და ბეჭდური შეკვეთების სტატუსი.",
      path: "/dashboard",
      noindex: true,
    });
    return { meta, links };
  },
  component: DashboardRoute,
});

function DashboardRoute() {
  const { bookId } = Route.useSearch();
  return <DashboardScreen celebrationBookId={bookId} />;
}
