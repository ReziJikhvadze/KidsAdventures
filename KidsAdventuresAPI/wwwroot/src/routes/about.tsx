import { createFileRoute } from "@tanstack/react-router";

import { LegalPageShell } from "@/components/adventrya/LegalPageShell";
import { LegalDocument } from "@/components/legal/LegalDocument";
import { aboutIntro, aboutSections } from "@/content/legal/about";
import { BRAND_NAME } from "@/lib/brand";
import { buildPageMeta } from "@/lib/seo";

export const Route = createFileRoute("/about")({
  head: () => {
    const { meta, links } = buildPageMeta({
      title: `ჩვენ შესახებ — ${BRAND_NAME}`,
      description: `ვინ ვართ და რას ვქმნით — პერსონალური საბავშვო წიგნები, სადაც მთავარი გმირი თქვენი ბავშვია.`,
      path: "/about",
    });
    return { meta, links };
  },
  component: AboutPage,
});

function AboutPage() {
  return (
    <LegalPageShell>
      <LegalDocument title="ჩვენ შესახებ" intro={aboutIntro} sections={aboutSections} />
    </LegalPageShell>
  );
}
