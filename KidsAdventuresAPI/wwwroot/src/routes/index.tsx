import { createFileRoute } from "@tanstack/react-router";
import { Nav } from "@/components/site/Nav";
import { Hero } from "@/components/site/Hero";
import { Themes } from "@/components/site/Themes";
import { Preview } from "@/components/site/Preview";
import { Benefits } from "@/components/site/Benefits";
import { Pricing } from "@/components/site/Pricing";
import { FAQ } from "@/components/site/FAQ";
import { Generator } from "@/components/site/Generator";
import { Grandparents } from "@/components/site/Grandparents";
import { FinalCTA } from "@/components/site/FinalCTA";
import { SeoContent } from "@/components/site/SeoContent";
import { Footer } from "@/components/site/Footer";
import { JsonLd } from "@/components/seo/JsonLd";
import { isStoryThemeId } from "@/lib/themes";
import { buildPageMeta } from "@/lib/seo";
import { FAQ_ITEMS } from "@/lib/faq";
import {
  buildFaqSchema,
  buildOrganizationSchema,
  buildWebSiteSchema,
} from "@/lib/structured-data";

type HomeSearch = {
  theme?: string;
};

export const Route = createFileRoute("/")({
  validateSearch: (search: Record<string, unknown>): HomeSearch => {
    const theme = typeof search.theme === "string" ? search.theme : undefined;
    return { theme };
  },
  head: () => {
    const { meta, links } = buildPageMeta({
      title: "Personalized Children's Books & Kids Adventure Stories | Adventrya Books",
      description:
        "Create personalized children's books starring your child — custom illustrated adventure storybooks. Child education, screen-free parenting, free preview & printable PDF.",
      path: "/",
    });
    return { meta, links };
  },
  component: Landing,
});

function Landing() {
  const { theme } = Route.useSearch();
  const initialTheme = theme && isStoryThemeId(theme) ? theme : null;

  return (
    <div className="min-h-screen bg-background text-foreground">
      <JsonLd
        data={[
          buildWebSiteSchema(),
          buildOrganizationSchema(),
          buildFaqSchema([...FAQ_ITEMS]),
        ]}
      />
      <Nav />
      <main>
        <Hero />
        <Generator initialTheme={initialTheme} />
        <Themes />
        <Preview />
        <Benefits />
        <Grandparents />
        <Pricing />
        <FAQ />
        <SeoContent />
        <FinalCTA />
      </main>
      <Footer />
    </div>
  );
}
