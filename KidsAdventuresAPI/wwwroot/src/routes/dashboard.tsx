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
  /**
   * Which child's space to open, when the parent is coming back from somewhere that knew.
   *
   * The world picker's back arrow carries it: the cabinet otherwise opens on whoever owns the
   * newest book, which for a family with two children is not the one whose button was pressed.
   * A hint only, like `bookId` — an id that names nobody in this family is ignored.
   */
  characterId?: string;
};

export const Route = createFileRoute("/dashboard")({
  validateSearch: (search: Record<string, unknown>): DashboardSearch => ({
    bookId: typeof search.bookId === "string" ? search.bookId : undefined,
    characterId: typeof search.characterId === "string" ? search.characterId : undefined,
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
  const { bookId, characterId } = Route.useSearch();
  return <DashboardScreen celebrationBookId={bookId} preferredCharacterId={characterId} />;
}
