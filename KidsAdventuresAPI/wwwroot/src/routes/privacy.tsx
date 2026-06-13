import { createFileRoute, Link } from "@tanstack/react-router";

import { LegalDocument } from "@/components/legal/LegalDocument";
import { Nav } from "@/components/site/Nav";
import { Footer } from "@/components/site/Footer";
import { privacyIntro, privacySections } from "@/content/legal/privacy";
import { BRAND_NAME } from "@/lib/brand";

export const Route = createFileRoute("/privacy")({
  head: () => ({
    meta: [
      { title: `Privacy Policy — ${BRAND_NAME}` },
      {
        name: "description",
        content: `How ${BRAND_NAME} collects, uses, and protects personal data — including photos, AI processing, and your GDPR rights.`,
      },
    ],
  }),
  component: PrivacyPage,
});

function PrivacyPage() {
  return (
    <div className="min-h-screen bg-background text-foreground">
      <Nav />
      <main className="pt-4">
        <div className="mx-auto max-w-3xl px-4 sm:px-6 pb-4">
          <Link
            to="/"
            className="text-sm text-muted-foreground hover:text-foreground transition-colors"
          >
            ← Back to home
          </Link>
        </div>
        <LegalDocument title="Privacy Policy" intro={privacyIntro} sections={privacySections} />
      </main>
      <Footer />
    </div>
  );
}
