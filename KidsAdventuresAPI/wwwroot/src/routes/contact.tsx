import { createFileRoute, Link } from "@tanstack/react-router";

import { Nav } from "@/components/site/Nav";
import { Footer } from "@/components/site/Footer";
import { Contact } from "@/components/site/Contact";
import { JsonLd } from "@/components/seo/JsonLd";
import { BRAND_NAME } from "@/lib/brand";
import { buildPageMeta } from "@/lib/seo";
import { buildBreadcrumbSchema, buildContactPageSchema } from "@/lib/structured-data";

export const Route = createFileRoute("/contact")({
  head: () => {
    const { meta, links } = buildPageMeta({
      title: `Contact us — ${BRAND_NAME}`,
      description:
        "Get in touch with the Adventrya Books team — questions about stories, book credits, printing, or personalized gifts.",
      path: "/contact",
    });
    return { meta, links };
  },
  component: ContactPage,
});

function ContactPage() {
  return (
    <div className="min-h-screen bg-background text-foreground">
      <JsonLd
        data={[
          buildContactPageSchema(),
          buildBreadcrumbSchema([
            { name: "Home", path: "/" },
            { name: "Contact", path: "/contact" },
          ]),
        ]}
      />
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
        <Contact />
      </main>
      <Footer />
    </div>
  );
}
