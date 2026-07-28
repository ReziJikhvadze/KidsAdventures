import { createFileRoute } from "@tanstack/react-router";

import { LegalPageShell } from "@/components/adventrya/LegalPageShell";
import { LegalDocument } from "@/components/legal/LegalDocument";
import { termsIntro, termsSections } from "@/content/legal/terms";
import { BRAND_NAME } from "@/lib/brand";
import { buildPageMeta } from "@/lib/seo";

export const Route = createFileRoute("/terms")({
  head: () => {
    const { meta, links } = buildPageMeta({
      title: `წესები და პირობები — ${BRAND_NAME}`,
      description: `${BRAND_NAME}-ის გამოყენების პირობები — საბავშვო წიგნები, მშობლის პასუხისმგებლობა და შინაარსის განმარტებები.`,
      path: "/terms",
    });
    return { meta, links };
  },
  component: TermsPage,
});

function TermsPage() {
  return (
    <LegalPageShell>
      <LegalDocument title="Terms & Conditions" intro={termsIntro} sections={termsSections} />
    </LegalPageShell>
  );
}
