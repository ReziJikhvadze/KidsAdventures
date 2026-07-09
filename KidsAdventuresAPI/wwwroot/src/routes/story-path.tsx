import { createFileRoute, Link } from "@tanstack/react-router";
import { isStoryThemeId } from "@/lib/themes";
import { THEME_ID_TO_API, type ThemeType } from "@/lib/api/types";
import { Nav } from "@/components/site/Nav";
import { Footer } from "@/components/site/Footer";
import { StoryPathView } from "@/components/story-path/StoryPathView";
import { BRAND_NAME } from "@/lib/brand";
import { buildPageMeta } from "@/lib/seo";

type StoryPathSearch = {
  child?: string;
  theme?: string;
};

function parseTheme(value: string | undefined): ThemeType | undefined {
  if (!value) return undefined;
  const lower = value.toLowerCase();
  if (isStoryThemeId(lower)) return THEME_ID_TO_API[lower];
  const direct = value as ThemeType;
  if (["Airplanes", "Dinosaurs", "Space", "Pirates", "Animals"].includes(direct)) {
    return direct;
  }
  return undefined;
}

export const Route = createFileRoute("/story-path")({
  validateSearch: (search: Record<string, unknown>): StoryPathSearch => ({
    child: typeof search.child === "string" ? search.child : undefined,
    theme: typeof search.theme === "string" ? search.theme : undefined,
  }),
  head: () => {
    const { meta, links } = buildPageMeta({
      title: `Story Path — ${BRAND_NAME}`,
      description:
        "Follow your child's illustrated adventure on a gentle story map — page by page, with campfire moments together.",
      path: "/story-path",
      noindex: true,
    });
    return { meta, links };
  },
  component: StoryPathPage,
});

function StoryPathPage() {
  const { child, theme } = Route.useSearch();
  const initialTheme = parseTheme(theme);

  return (
    <div className="min-h-screen bg-background text-foreground">
      <Nav />
      <main className="pt-4">
        <div className="mx-auto max-w-5xl px-4 sm:px-6 pb-4">
          <Link
            to="/my-packs"
            className="text-sm text-muted-foreground hover:text-foreground transition-colors"
          >
            ← My Books
          </Link>
        </div>
        <StoryPathView initialChildId={child} initialTheme={initialTheme} />
      </main>
      <Footer />
    </div>
  );
}
