import { createFileRoute } from "@tanstack/react-router";

import { LegalPageShell } from "@/components/adventrya/LegalPageShell";
import { LegalDocument } from "@/components/legal/LegalDocument";
import { privacyIntro, privacySections } from "@/content/legal/privacy";
import { BRAND_NAME } from "@/lib/brand";
import { buildPageMeta } from "@/lib/seo";

export const Route = createFileRoute("/privacy")({
  head: () => {
    const { meta, links } = buildPageMeta({
      title: `კონფიდენციალურობა — ${BRAND_NAME}`,
      description: `როგორ იცავს ${BRAND_NAME} პერსონალურ მონაცემებს — ფოტოები, AI დამუშავება და თქვენი უფლებები.`,
      path: "/privacy",
    });
    return { meta, links };
  },
  component: PrivacyPage,
});

function PrivacyPage() {
  return (
    <LegalPageShell>
      {/* Written in English under an app that declares `<html lang="ka">`, so it says so. */}
      <LegalDocument
        lang="en"
        title="Privacy Policy"
        intro={privacyIntro}
        sections={privacySections}
      />
    </LegalPageShell>
  );
}
