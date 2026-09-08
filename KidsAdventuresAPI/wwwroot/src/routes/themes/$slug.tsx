import { createFileRoute, redirect } from "@tanstack/react-router";

import { isWorldId } from "@/lib/worlds";

/**
 * Per-theme English SEO pages retired — land on the demo first-map picker.
 *
 * With the world the visitor asked for still chosen.
 *
 * The redirect used to drop it: `/themes/dinosaurs` — a link that says which world it is about,
 * and the address the sitemap still publishes for all six — arrived at a picker with nothing
 * selected, no button to press, and a page that announces "no world chosen yet". Pressing a
 * named world and being asked to choose one is the whole of the report this fixes.
 *
 * The six slugs are the six world ids (see `lib/themes.ts`), so no table is needed — but the
 * value lands in an address the picker reads, so it is checked against the list rather than
 * forwarded because it happened to be in the URL.
 */
export const Route = createFileRoute("/themes/$slug")({
  beforeLoad: ({ params }) => {
    throw redirect({
      to: "/themes",
      search: isWorldId(params.slug) ? { world: params.slug } : undefined,
    });
  },
});
