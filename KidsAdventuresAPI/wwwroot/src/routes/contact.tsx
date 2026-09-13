import { createFileRoute } from "@tanstack/react-router";

import { LegalPageShell } from "@/components/adventrya/LegalPageShell";
import { Contact } from "@/components/site/Contact";
import { JsonLd } from "@/components/seo/JsonLd";
import { BRAND_NAME } from "@/lib/brand";
import { buildPageMeta } from "@/lib/seo";
import { buildBreadcrumbSchema, buildContactPageSchema } from "@/lib/structured-data";

export const Route = createFileRoute("/contact")({
  /*
    The book a parent arrived here about, when they arrived from one.

    The dashboard's failed-book card sends them here rather than to a mail client, and the id is
    what stops the first reply being "which book?". Optional and unvalidated beyond its shape: this
    page is also reached from the footer by people with nothing to reference.
  */
  validateSearch: (search: Record<string, unknown>): { bookId?: string } => ({
    bookId:
      typeof search.bookId === "string" && search.bookId.length > 0 ? search.bookId : undefined,
  }),
  head: () => {
    const { meta, links } = buildPageMeta({
      title: `კონტაქტი - ${BRAND_NAME}`,
      description:
        "დაგვიკავშირდი Beki გუნდს - კითხვები ამბების, ბეჭდვის ან პირადი საჩუქრების შესახებ.",
      path: "/contact",
    });
    return { meta, links };
  },
  component: ContactPage,
});

function ContactPage() {
  const { bookId } = Route.useSearch();

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
      <Contact bookId={bookId} />
    </LegalPageShell>
  );
}
