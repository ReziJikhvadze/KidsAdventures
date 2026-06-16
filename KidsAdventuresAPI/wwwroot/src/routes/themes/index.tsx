import { createFileRoute, Link } from "@tanstack/react-router";
import { ArrowRight } from "lucide-react";

import { JsonLd } from "@/components/seo/JsonLd";
import { Nav } from "@/components/site/Nav";
import { Footer } from "@/components/site/Footer";
import { BRAND_NAME } from "@/lib/brand";
import { buildPageMeta } from "@/lib/seo";
import { STORY_THEMES } from "@/lib/themes";
import {
  buildBreadcrumbSchema,
  buildGiftGuideItemListSchema,
  buildWebPageSchema,
} from "@/lib/structured-data";

const PAGE_TITLE = `Kids Adventure Book Themes — Personalized Children's Stories`;
const PAGE_DESCRIPTION =
  "Choose a personalized children's book theme: dinosaurs, space, pirates, animals, or airplanes. Custom illustrated adventure books for kids ages 3–12 — free preview, printable PDF.";

export const Route = createFileRoute("/themes/")({
  head: () => {
    const { meta, links } = buildPageMeta({
      title: `${PAGE_TITLE} | ${BRAND_NAME}`,
      description: PAGE_DESCRIPTION,
      path: "/themes",
    });
    return { meta, links };
  },
  component: ThemesIndexPage,
});

function ThemesIndexPage() {
  return (
    <div className="min-h-screen bg-background text-foreground">
      <JsonLd
        data={[
          buildWebPageSchema({
            path: "/themes",
            title: PAGE_TITLE,
            description: PAGE_DESCRIPTION,
          }),
          buildGiftGuideItemListSchema(
            STORY_THEMES.map((theme) => ({
              name: `${theme.name} adventure books for kids`,
              description: theme.seoDescription,
            })),
          ),
          buildBreadcrumbSchema([
            { name: "Home", path: "/" },
            { name: "Themes", path: "/themes" },
          ]),
        ]}
      />
      <Nav />
      <main className="mx-auto max-w-5xl px-6 py-16 md:py-24">
        <p className="text-sm font-semibold text-primary tracking-wide uppercase">Book themes</p>
        <h1 className="mt-3 font-display text-4xl md:text-5xl font-bold text-balance">
          Personalized adventure books for every kind of kid
        </h1>
        <p className="mt-5 text-lg text-muted-foreground max-w-3xl">
          Pick a theme your child already loves — we personalize the story with their name, age,
          and optional photo. Each theme is a full illustrated kids adventure book you can read
          online or print at home for bedtime, learning time, or gifts.
        </p>

        <div className="mt-12 grid sm:grid-cols-2 gap-6">
          {STORY_THEMES.map((theme) => (
            <Link
              key={theme.slug}
              to="/themes/$slug"
              params={{ slug: theme.slug }}
              className="group rounded-3xl border border-border bg-card overflow-hidden shadow-soft hover:shadow-card transition"
            >
              <img
                src={theme.image}
                alt={`${theme.name} personalized children's adventure book theme`}
                className="w-full h-44 object-cover"
              />
              <div className="p-6">
                <h2 className="font-display text-2xl font-semibold group-hover:text-primary transition">
                  {theme.name} adventure books
                </h2>
                <p className="mt-2 text-sm text-muted-foreground">{theme.intro}</p>
                <span className="mt-4 inline-flex items-center gap-1 text-sm font-semibold text-primary">
                  Create {theme.name.toLowerCase()} book
                  <ArrowRight className="h-4 w-4 transition group-hover:translate-x-0.5" />
                </span>
              </div>
            </Link>
          ))}
        </div>

        <div className="mt-14 rounded-3xl bg-secondary/50 border border-border p-8">
          <h2 className="font-display text-2xl font-bold">Not sure which theme to pick?</h2>
          <p className="mt-3 text-muted-foreground">
            Dinosaur and space books are top picks for boys and girls ages 4–10. Animal themes
            work well for younger readers. Try the free 2-page preview on any theme — no card
            required.
          </p>
          <Link
            to="/"
            hash="generator"
            className="mt-5 inline-flex items-center gap-2 rounded-full bg-primary text-primary-foreground px-5 py-2.5 text-sm font-semibold hover:opacity-90 transition"
          >
            Start free preview
            <ArrowRight className="h-4 w-4" />
          </Link>
        </div>
      </main>
      <Footer />
    </div>
  );
}
