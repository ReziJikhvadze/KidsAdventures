import { createFileRoute, Link } from "@tanstack/react-router";

import { Nav } from "@/components/site/Nav";
import { Footer } from "@/components/site/Footer";
import { MyPacks } from "@/components/site/MyPacks";
import { BRAND_NAME } from "@/lib/brand";
import { buildPageMeta } from "@/lib/seo";

export const Route = createFileRoute("/my-packs")({
  head: () => {
    const { meta, links } = buildPageMeta({
      title: `My books — ${BRAND_NAME}`,
      description: "Read your stories and download illustrated storybook PDFs.",
      path: "/my-packs",
      noindex: true,
    });
    return { meta, links };
  },
  component: MyPacksPage,
});

function MyPacksPage() {
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
        <MyPacks />
      </main>
      <Footer />
    </div>
  );
}
