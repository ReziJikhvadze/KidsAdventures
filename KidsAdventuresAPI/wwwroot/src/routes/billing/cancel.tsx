import { createFileRoute, Link } from "@tanstack/react-router";

import { Nav } from "@/components/site/Nav";
import { Footer } from "@/components/site/Footer";
import { BRAND_NAME } from "@/lib/brand";
import { buildPageMeta } from "@/lib/seo";

export const Route = createFileRoute("/billing/cancel")({
  head: () => {
    const { meta, links } = buildPageMeta({
      title: `Checkout cancelled — ${BRAND_NAME}`,
      description: "Your checkout was cancelled. No charge was made.",
      path: "/billing/cancel",
      noindex: true,
    });
    return { meta, links };
  },
  component: BillingCancel,
});

function BillingCancel() {
  return (
    <div className="min-h-screen bg-background text-foreground">
      <Nav />
      <main className="mx-auto max-w-lg px-6 py-24 text-center">
        <h1 className="font-display text-3xl font-bold">Checkout cancelled</h1>
        <p className="mt-4 text-muted-foreground">
          No charge was made. You can still create free stories — buy book credits when you are ready
          for an illustrated PDF.
        </p>
        <div className="mt-8 flex flex-col sm:flex-row gap-3 justify-center">
          <Link
            to="/"
            hash="pricing"
            className="inline-flex items-center justify-center rounded-full bg-primary text-primary-foreground px-6 py-3 font-semibold hover:opacity-90 transition"
          >
            View book packs
          </Link>
          <Link
            to="/"
            hash="generator"
            className="inline-flex items-center justify-center rounded-full border border-border px-6 py-3 font-semibold hover:bg-secondary transition"
          >
            Create a story
          </Link>
        </div>
      </main>
      <Footer />
    </div>
  );
}
