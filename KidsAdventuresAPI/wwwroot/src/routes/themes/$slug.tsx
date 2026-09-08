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
      /*
        `from=top` as well as the world.

        Without it the picker's back arrow reads `?world=` with no origin as the home page's
        book gallery — right for the covers there, which link exactly that way, and wrong for
        somebody who arrived from a search result and has never seen the page. The top of the
        home page is where an arrival from outside belongs.
      */
      search: isWorldId(params.slug) ? { world: params.slug, from: "top" } : { from: "top" },
    });
  },
});
