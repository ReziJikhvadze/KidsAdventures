import { createFileRoute } from "@tanstack/react-router";

import { ReaderScreen } from "@/components/adventrya/reader/ReaderScreen";
import { BRAND_NAME } from "@/lib/brand";
import { buildPageMeta } from "@/lib/seo";

export const Route = createFileRoute("/reader/$bookId")({
  head: () => {
    const { meta, links } = buildPageMeta({
      title: `Online Reader — ${BRAND_NAME}`,
      description: "წაიკითხე პერსონალიზებული წიგნი ონლაინ, გვერდ-გვერდ.",
      path: "/reader",
      noindex: true,
    });
    return { meta, links };
  },
  component: ReaderScreen,
});
