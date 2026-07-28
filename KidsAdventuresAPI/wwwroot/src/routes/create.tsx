import { createFileRoute } from "@tanstack/react-router";

import { JourneyScreen } from "@/components/adventrya/journey/JourneyScreen";
import { BRAND_NAME } from "@/lib/brand";
import { buildPageMeta } from "@/lib/seo";

export const Route = createFileRoute("/create")({
  head: () => {
    const { meta, links } = buildPageMeta({
      title: `შექმენი წიგნი — ${BRAND_NAME}`,
      description:
        "გაგვაცანი პატარა გმირი, აირჩიე სამყარო და ნახე პერსონალიზებული Preview გადახდამდე.",
      path: "/create",
      noindex: true,
    });
    return { meta, links };
  },
  component: JourneyScreen,
});
