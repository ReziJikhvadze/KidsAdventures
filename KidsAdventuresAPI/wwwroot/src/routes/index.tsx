import { createFileRoute } from "@tanstack/react-router";

import { LandingPage } from "@/components/adventrya/landing/LandingPage";
import { BRAND_NAME } from "@/lib/brand";
import { buildPageMeta } from "@/lib/seo";

type HomeSearch = {
  theme?: string;
};

export const Route = createFileRoute("/")({
  validateSearch: (search: Record<string, unknown>): HomeSearch => {
    const theme = typeof search.theme === "string" ? search.theme : undefined;
    return { theme };
  },
  head: () => {
    const { meta, links } = buildPageMeta({
      title: `პერსონალიზებული საბავშვო წიგნები | ${BRAND_NAME}`,
      description:
        "შექმენი პერსონალიზებული ილუსტრირებული წიგნი, სადაც მთავარი გმირი შენი ბავშვია — Digital 14 ₾-დან, Printed + Digital 79 ₾.",
      path: "/",
    });
    return { meta, links };
  },
  component: LandingPage,
});
