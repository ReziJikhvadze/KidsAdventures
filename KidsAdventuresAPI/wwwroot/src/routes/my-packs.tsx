import { createFileRoute, Link } from "@tanstack/react-router";

import { Nav } from "@/components/site/Nav";
import { Footer } from "@/components/site/Footer";
import { MyPacks } from "@/components/site/MyPacks";

export const Route = createFileRoute("/my-packs")({
  head: () => ({
    meta: [
      { title: "My adventure packs — AdventurePacks" },
      {
        name: "description",
        content: "View and download your generated printable adventure packs.",
      },
    ],
  }),
  component: MyPacksPage,
});

function MyPacksPage() {
  return (
    <div className="min-h-screen bg-background text-foreground">
      <Nav />
      <main className="pt-4">
        <div className="mx-auto max-w-5xl px-6 pb-4">
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
