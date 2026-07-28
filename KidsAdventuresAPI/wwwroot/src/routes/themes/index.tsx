import { createFileRoute } from "@tanstack/react-router";

import { WorldStage } from "@/components/adventrya/journey/WorldStage";
import { BRAND_NAME } from "@/lib/brand";
import { useJourneyDraft } from "@/lib/journey/draft";
import { buildPageMeta } from "@/lib/seo";

/**
 * Partner Demo first-map theme picker at `/themes`.
 * Continues into `/create#preview` with the selected world.
 */
export const Route = createFileRoute("/themes/")({
  head: () => {
    const { meta, links } = buildPageMeta({
      title: `აირჩიე სამყარო — ${BRAND_NAME}`,
      description: "აირჩიე პირველი თავგადასავლის სამყარო რუკაზე.",
      path: "/themes",
      noindex: true,
    });
    return { meta, links };
  },
  component: ThemesPage,
});

function ThemesPage() {
  const [draft, setDraft] = useJourneyDraft();
  return <WorldStage draft={draft} onChange={setDraft} />;
}
