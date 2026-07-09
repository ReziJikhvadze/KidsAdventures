import { createFileRoute, Link, notFound } from "@tanstack/react-router";
import { ArrowRight, Check } from "lucide-react";

import { JsonLd } from "@/components/seo/JsonLd";
import { Nav } from "@/components/site/Nav";
import { Footer } from "@/components/site/Footer";
import { STORY_THEMES, getThemeBySlug, type StoryTheme } from "@/lib/themes";
import { buildPageMeta, absoluteUrl } from "@/lib/seo";
import {
  buildBreadcrumbSchema,
  buildFaqSchema,
  buildThemeProductSchema,
  buildWebPageSchema,
} from "@/lib/structured-data";

/** Per-theme FAQ — unique copy per page so each FAQPage is original content, not boilerplate. */
function buildThemeFaqs(theme: StoryTheme): { q: string; a: string }[] {
  const lower = theme.name.toLowerCase();
  return [
    {
      q: `How do I make a personalized ${lower} book for my child?`,
      a: `Enter your child's name and age, pick the ${theme.name} theme, and optionally upload a photo. We write a full personalized ${lower} story for free in minutes — read it instantly or export a printable PDF.`,
    },
    {
      q: `Is the ${lower} storybook really free to try?`,
      a: `Yes. Your first complete ${lower} storybook is free and fully illustrated. Each additional illustrated storybook is a one-time $4.99 — the printable PDF download is always free.`,
    },
    {
      q: `What age is this ${lower} adventure book good for?`,
      a: `Our ${lower} stories are kid-safe and adapt to your child's age, ideal for roughly ages 3–10 for bedtime reading, early literacy, and screen-free learning.`,
    },
    {
      q: `Can I print the ${lower} book at home?`,
      a: `Absolutely. Export a print-ready PDF and print on any standard color printer (A4 or US Letter) — perfect for birthdays, holidays, and grandparent gifts.`,
    },
  ];
}

export const Route = createFileRoute("/themes/$slug")({
  head: ({ params }) => {
    const theme = getThemeBySlug(params.slug);
    if (!theme) return { meta: [{ title: "Theme not found" }] };
    const { meta, links } = buildPageMeta({
      title: theme.seoTitle,
      description: theme.seoDescription,
      path: `/themes/${theme.slug}`,
      image: absoluteUrl(theme.image),
      // Preload the hero image (LCP) using the exact <img src> so paint isn't blocked.
      preloadImage: theme.image,
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
  const lower = theme.name.toLowerCase();
  const faqs = buildThemeFaqs(theme);

  const steps = [
    {
      title: "1. Add your child",
      body: `Enter their name and age, then choose the ${theme.name} theme.`,
    },
    {
      title: "2. Preview free",
      body: "We write the full personalized story instantly — read it before you pay.",
    },
    {
      title: "3. Unlock for $4.99",
      body: "Illustrate every page and export a free printable PDF.",
    },
  ];

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
            { name: "Themes", path: "/themes" },
            { name: theme.name, path: `/themes/${theme.slug}` },
          ]),
          buildFaqSchema(faqs),
        ]}
      />
      <Nav />
      <main>
        {/* Visible breadcrumb mirrors the BreadcrumbList schema (crawlable internal links). */}
        <nav aria-label="Breadcrumb" className="mx-auto max-w-5xl px-6 pt-8 pb-4">
          <ol className="flex flex-wrap items-center gap-1.5 text-sm text-muted-foreground">
            <li>
              <Link to="/" className="hover:text-foreground transition-colors">
                Home
              </Link>
            </li>
            <li aria-hidden>/</li>
            <li>
              <Link to="/themes" className="hover:text-foreground transition-colors">
                Adventure themes
              </Link>
            </li>
            <li aria-hidden>/</li>
            <li className="text-foreground font-medium" aria-current="page">
              {theme.name}
            </li>
          </ol>
        </nav>

        <article className="mx-auto max-w-5xl px-6 pb-16 md:pb-24">
          {/* HERO — LCP zone */}
          <section className="grid md:grid-cols-[1fr_1.1fr] gap-10 items-center">
            <div
              className="rounded-3xl border border-border overflow-hidden shadow-card aspect-square max-w-md"
              style={{
                background: `color-mix(in oklab, ${theme.tint} 35%, var(--card))`,
              }}
            >
              <img
                src={theme.image}
                alt={`Personalized ${lower} adventure storybook for kids, starring your child as the hero`}
                width={768}
                height={768}
                fetchPriority="high"
                decoding="async"
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
                Create your {lower} story
                <ArrowRight className="h-4 w-4 transition group-hover:translate-x-0.5" />
              </Link>
            </div>
          </section>

          {/* WHAT'S INSIDE */}
          <section className="mt-16">
            <h2 className="font-display text-2xl md:text-3xl font-semibold">
              What's inside your personalized {lower} book
            </h2>
            <div className="mt-6 prose prose-neutral max-w-none text-muted-foreground space-y-4">
              {theme.paragraphs.map((paragraph) => (
                <p key={paragraph} className="text-pretty leading-relaxed">
                  {paragraph}
                </p>
              ))}
            </div>
          </section>

          {/* HOW IT WORKS */}
          <section className="mt-16">
            <h2 className="font-display text-2xl md:text-3xl font-semibold">
              How to create a {lower} storybook
            </h2>
            <ol className="mt-6 grid gap-4 sm:grid-cols-3">
              {steps.map((step) => (
                <li key={step.title} className="rounded-2xl border border-border bg-card p-5">
                  <h3 className="font-display font-semibold">{step.title}</h3>
                  <p className="mt-1.5 text-sm text-muted-foreground">{step.body}</p>
                </li>
              ))}
            </ol>
          </section>

          {/* FAQ — mirrors FAQPage schema */}
          <section className="mt-16" aria-labelledby="faq-heading">
            <h2 id="faq-heading" className="font-display text-2xl md:text-3xl font-semibold">
              {theme.name} book — frequently asked questions
            </h2>
            <dl className="mt-6 space-y-5">
              {faqs.map((faq) => (
                <div key={faq.q} className="rounded-2xl border border-border bg-card p-5">
                  <dt className="font-display font-semibold">{faq.q}</dt>
                  <dd className="mt-1.5 text-sm text-muted-foreground leading-relaxed">{faq.a}</dd>
                </div>
              ))}
            </dl>
          </section>

          {/* INTERNAL LINKS — descriptive, keyword-rich anchors */}
          <aside className="mt-16" aria-labelledby="related-heading">
            <h2 id="related-heading" className="font-display text-2xl font-semibold">
              Explore other personalized adventure books
            </h2>
            <ul className="mt-6 grid sm:grid-cols-2 lg:grid-cols-4 gap-4">
              {otherThemes.map((t) => (
                <li key={t.slug}>
                  <Link
                    to="/themes/$slug"
                    params={{ slug: t.slug }}
                    className="block rounded-2xl border border-border bg-card p-4 hover:shadow-soft transition"
                    title={`Personalized ${t.name.toLowerCase()} book for kids`}
                  >
                    <span className="font-display font-semibold">
                      Personalized {t.name.toLowerCase()} book
                    </span>
                    <span className="mt-0.5 block text-sm text-muted-foreground">{t.shortDesc}</span>
                  </Link>
                </li>
              ))}
            </ul>
            <p className="mt-6 text-sm text-muted-foreground">
              New to custom storybooks? Read our guide to{" "}
              <Link to="/personalized-childrens-books" className="text-primary hover:underline">
                personalized children's books
              </Link>{" "}
              or browse the{" "}
              <Link to="/gift-guide" className="text-primary hover:underline">
                personalized storybook gift guide
              </Link>
              .
            </p>
          </aside>
        </article>
      </main>
      <Footer />
    </div>
  );
}
