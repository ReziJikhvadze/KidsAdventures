import { createFileRoute } from "@tanstack/react-router";

import { LegalPageShell } from "@/components/adventrya/LegalPageShell";
import { Contact } from "@/components/site/Contact";
import { MerchantDetails } from "@/components/site/MerchantDetails";
import { JsonLd } from "@/components/seo/JsonLd";
import { BRAND_NAME } from "@/lib/brand";
import { buildPageMeta } from "@/lib/seo";
import { buildBreadcrumbSchema, buildContactPageSchema } from "@/lib/structured-data";

export const Route = createFileRoute("/contact")({
  head: () => {
    const { meta, links } = buildPageMeta({
      title: `კონტაქტი — ${BRAND_NAME}`,
      description:
        "დაგვიკავშირდი Beki გუნდს — კითხვები ამბების, ბეჭდვის ან პირადი საჩუქრების შესახებ.",
      path: "/contact",
    });
    return { meta, links };
  },
  component: ContactPage,
});

function ContactPage() {
  return (
    <LegalPageShell>
      <JsonLd
        data={[
          buildContactPageSchema(),
          buildBreadcrumbSchema([
            { name: "Home", path: "/" },
            { name: "Contact", path: "/contact" },
          ]),
        ]}
      />
      <Contact />
      <MerchantDetails />
    </LegalPageShell>
  );
}
