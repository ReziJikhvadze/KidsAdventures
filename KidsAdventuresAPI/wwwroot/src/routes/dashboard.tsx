import { createFileRoute } from "@tanstack/react-router";

import { DashboardScreen } from "@/components/adventrya/dashboard/DashboardScreen";
import { BRAND_NAME } from "@/lib/brand";
import { buildPageMeta } from "@/lib/seo";

export const Route = createFileRoute("/dashboard")({
  head: () => {
    const { meta, links } = buildPageMeta({
      title: `Parent Dashboard — ${BRAND_NAME}`,
      description: "ბავშვების პროფილები, წიგნების ბიბლიოთეკა და ბეჭდური შეკვეთების სტატუსი.",
      path: "/dashboard",
      noindex: true,
    });
    return { meta, links };
  },
  component: DashboardScreen,
});
