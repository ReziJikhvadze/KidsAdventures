import { createFileRoute, Link, notFound } from "@tanstack/react-router";
import { ArrowRight, Check } from "lucide-react";

import { JsonLd } from "@/components/seo/JsonLd";
import { Nav } from "@/components/site/Nav";
import { Footer } from "@/components/site/Footer";
import { STORY_THEMES, getThemeBySlug } from "@/lib/themes";
import { buildPageMeta, absoluteUrl } from "@/lib/seo";
import {
  buildBreadcrumbSchema,
  buildThemeProductSchema,
  buildWebPageSchema,
} from "@/lib/structured-data";

export const Route = createFileRoute("/themes/$slug")({
  head: ({ params }) => {
    const theme = getThemeBySlug(params.slug);
    if (!theme) return { meta: [{ title: "Theme not found" }] };
    const { meta, links } = buildPageMeta({
      title: theme.seoTitle,
      description: theme.seoDescription,
      path: `/themes/${theme.slug}`,
      image: absoluteUrl(theme.image),
    });
    return { meta, links };
  },
  component: ThemeLandingPage,
});

function ThemeLandingPage() {
  const { slug } = Route.useParams();
  const theme = getThemeBySlug(slug);
  if (!theme) throw notFound();

  const otherThemes = STORY_THEMES.filter((t) => t.slug !== theme.slug);

  return (
    <div className="min-h-screen bg-background text-foreground">
      <JsonLd
        data={[
          buildWebPageSchema({
            path: `/themes/${theme.slug}`,
            title: theme.seoTitle,
            description: theme.seoDescription,
          }),
          buildThemeProductSchema(theme),
          buildBreadcrumbSchema([
            { name: "Home", path: "/" },
            { name: "Themes", path: "/#themes" },
            { name: theme.name, path: `/themes/${theme.slug}` },
          ]),
        ]}
      />
      <Nav />
      <main>
        <div className="mx-auto max-w-5xl px-6 pt-8 pb-4">
          <Link
            to="/"
            hash="themes"
            className="text-sm text-muted-foreground hover:text-foreground transition-colors"
          >
            ← All themes
          </Link>
        </div>

        <section className="mx-auto max-w-5xl px-6 pb-16 md:pb-24">
          <div className="grid md:grid-cols-[1fr_1.1fr] gap-10 items-center">
            <div
              className="rounded-3xl border border-border overflow-hidden shadow-card aspect-square max-w-md"
              style={{
                background: `color-mix(in oklab, ${theme.tint} 35%, var(--card))`,
              }}
            >
              <img
                src={theme.image}
                alt={`${theme.name} theme illustration`}
                width={768}
                height={768}
                className="w-full h-full object-contain p-6"
              />
            </div>
            <div>
              <p className="text-sm font-semibold text-primary tracking-wide uppercase">
                {theme.name} theme
              </p>
              <h1 className="mt-3 font-display text-4xl md:text-5xl font-bold text-balance">
                {theme.heroHeading}
              </h1>
              <p className="mt-4 text-lg text-muted-foreground text-pretty">{theme.intro}</p>
              <ul className="mt-6 space-y-2">
                {theme.highlights.map((item) => (
                  <li key={item} className="flex items-start gap-2 text-sm text-muted-foreground">
                    <Check className="h-4 w-4 text-primary shrink-0 mt-0.5" />
                    {item}
                  </li>
                ))}
              </ul>
              <Link
                to="/"
                search={{ theme: theme.id }}
                hash="generator"
                className="group inline-flex mt-8 items-center gap-2 rounded-full bg-primary text-primary-foreground px-6 py-3.5 font-semibold shadow-soft hover:translate-y-[-1px] transition"
              >
                Create {theme.name.toLowerCase()} story
                <ArrowRight className="h-4 w-4 transition group-hover:translate-x-0.5" />
              </Link>
            </div>
          </div>

          <div className="mt-16 prose prose-neutral max-w-none text-muted-foreground space-y-4">
            {theme.paragraphs.map((paragraph) => (
              <p key={paragraph} className="text-pretty leading-relaxed">
                {paragraph}
              </p>
            ))}
          </div>

          <div className="mt-16">
            <h2 className="font-display text-2xl font-semibold">Explore other themes</h2>
            <div className="mt-6 grid sm:grid-cols-2 lg:grid-cols-4 gap-4">
              {otherThemes.map((t) => (
                <Link
                  key={t.slug}
                  to="/themes/$slug"
                  params={{ slug: t.slug }}
                  className="rounded-2xl border border-border bg-card p-4 hover:shadow-soft transition"
                >
                  <div className="font-display font-semibold">{t.name}</div>
                  <div className="text-sm text-muted-foreground">{t.shortDesc}</div>
                </Link>
              ))}
            </div>
          </div>
        </section>
      </main>
      <Footer />
    </div>
  );
}
