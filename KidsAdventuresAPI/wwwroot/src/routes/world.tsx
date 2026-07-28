import { createFileRoute } from "@tanstack/react-router";

import { ChildWorldScreen } from "@/components/adventrya/world/ChildWorldScreen";
import { BRAND_NAME } from "@/lib/brand";
import { buildPageMeta } from "@/lib/seo";

export const Route = createFileRoute("/world")({
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
  component: ChildWorldScreen,
});
