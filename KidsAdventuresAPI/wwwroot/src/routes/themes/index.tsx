import { createFileRoute } from "@tanstack/react-router";

import { WorldSelectorStage } from "@/components/adventrya/journey/WorldSelectorStage";
import { BRAND_NAME } from "@/lib/brand";
import { useJourneyDraft } from "@/lib/journey/draft";
import { buildPageMeta } from "@/lib/seo";

/**
 * The world picker at `/themes`, now the delivered selector.
 *
 * It carries its own header, its own progress rail and its own full-viewport layout, so the
 * app header that used to sit above it is gone from this route — two headers stacked on one
 * painting is the one thing the handoff asks not to do.
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
  return <WorldSelectorStage draft={draft} onChange={setDraft} />;
}
