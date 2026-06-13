import { createFileRoute, Link } from "@tanstack/react-router";

import { LegalDocument } from "@/components/legal/LegalDocument";
import { Nav } from "@/components/site/Nav";
import { Footer } from "@/components/site/Footer";
import { termsIntro, termsSections } from "@/content/legal/terms";
import { BRAND_NAME } from "@/lib/brand";
import { buildPageMeta } from "@/lib/seo";

export const Route = createFileRoute("/terms")({
  head: () => {
    const { meta, links } = buildPageMeta({
      title: `Terms & Conditions — ${BRAND_NAME}`,
      description: `Terms of use for ${BRAND_NAME} — AI storybooks for children, parental responsibility, and content disclaimers.`,
      path: "/terms",
    });
    return { meta, links };
  },
  component: TermsPage,
});

function TermsPage() {
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
        <LegalDocument title="Terms & Conditions" intro={termsIntro} sections={termsSections} />
      </main>
      <Footer />
    </div>
  );
}
