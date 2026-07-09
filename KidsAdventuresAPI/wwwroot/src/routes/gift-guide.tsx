import { createFileRoute, Link } from "@tanstack/react-router";
import { ArrowRight, Gift } from "lucide-react";

import { JsonLd } from "@/components/seo/JsonLd";
import { Nav } from "@/components/site/Nav";
import { Footer } from "@/components/site/Footer";
import { BRAND_NAME } from "@/lib/brand";
import { buildPageMeta } from "@/lib/seo";
import {
  buildBreadcrumbSchema,
  buildGiftGuideItemListSchema,
  buildWebPageSchema,
} from "@/lib/structured-data";

const GIFT_IDEAS = [
  {
    name: "Grandparent gift for grandkids",
    description:
      "A personalized printable storybook is easy to mail, email, or read over video call — no toy sizes to guess.",
  },
  {
    name: "Unique birthday gift for kids",
    description:
      "Add optional story wishes with a birthday message. Print and wrap the PDF for a one-of-a-kind party gift.",
  },
  {
    name: "Holiday stocking stuffer",
    description:
      "Pair a printed book with crayons. Space and dinosaur themes are holiday favorites.",
  },
  {
    name: "Sibling story set",
    description:
      "Use a 5- or 15-book pack so each child gets their own theme and hero story.",
  },
  {
    name: "Classroom or camp keepsake",
    description:
      "Teachers use book packs for end-of-year gifts — each child stars in their own adventure.",
  },
];

const GIFT_SECTIONS = [
  {
    heading: "Why personalized storybooks make great gifts",
    body: "Parents and grandparents want gifts that feel thoughtful without cluttering the toy box. A custom illustrated storybook starring the child checks every box: personal, printable, screen-free, and memorable.",
  },
  {
    heading: "Your first book is free before you wrap it",
    body: "Create your first full, illustrated 6-page storybook completely free. When it is perfect, each additional book is a one-time $4.99 — or buy a pack of credits during a sale and gift books throughout the year.",
  },
  {
    heading: "How book credits work",
    body: "Your first fully illustrated book is free. After that, each new illustrated storybook is $4.99 (one credit). Credits never expire — ideal for families with siblings or grandparents buying for multiple grandkids.",
  },
];

export const Route = createFileRoute("/gift-guide")({
  head: () => {
    const { meta, links } = buildPageMeta({
      title: `Personalized Storybook Gift Guide — ${BRAND_NAME}`,
      description:
        "Gift ideas for kids and grandkids: personalized illustrated storybooks, printable PDFs, birthday and holiday presents.",
      path: "/gift-guide",
    });
    return { meta, links };
  },
  component: GiftGuidePage,
});

function GiftGuidePage() {
  return (
    <div className="min-h-screen bg-background text-foreground">
      <JsonLd
        data={[
          buildWebPageSchema({
            path: "/gift-guide",
            title: `Personalized Storybook Gift Guide — ${BRAND_NAME}`,
            description:
              "Gift ideas for kids and grandkids: personalized illustrated storybooks and printable PDFs.",
          }),
          buildGiftGuideItemListSchema(GIFT_IDEAS),
          buildBreadcrumbSchema([
            { name: "Home", path: "/" },
            { name: "Gift guide", path: "/gift-guide" },
          ]),
        ]}
      />
      <Nav />
      <main>
        <div className="mx-auto max-w-3xl px-6 pt-8 pb-4">
          <Link
            to="/"
            className="text-sm text-muted-foreground hover:text-foreground transition-colors"
          >
            ← Back to home
          </Link>
        </div>

        <article className="mx-auto max-w-3xl px-6 pb-16 md:pb-24">
          <div className="inline-flex items-center gap-2 rounded-full border border-border bg-card/70 px-3 py-1 text-xs font-medium text-muted-foreground">
            <Gift className="h-3.5 w-3.5" />
            Gift guide
          </div>
          <h1 className="mt-5 font-display text-4xl md:text-5xl font-bold text-balance">
            Personalized storybook gifts kids actually keep
          </h1>
          <p className="mt-4 text-lg text-muted-foreground text-pretty">
            A custom illustrated adventure starring your child or grandchild — printable at home,
            memorable forever. Perfect for birthdays, holidays, and long-distance families.
          </p>

          <ul className="mt-10 space-y-4">
            {GIFT_IDEAS.map((idea) => (
              <li
                key={idea.name}
                className="rounded-2xl border border-border bg-card p-5 shadow-soft"
              >
                <h2 className="font-display text-xl font-semibold">{idea.name}</h2>
                <p className="mt-2 text-muted-foreground text-pretty">{idea.description}</p>
              </li>
            ))}
          </ul>

          <div className="mt-12 space-y-6 text-muted-foreground">
            {GIFT_SECTIONS.map((section) => (
              <section key={section.heading}>
                <h2 className="font-display text-2xl font-semibold text-foreground">
                  {section.heading}
                </h2>
                <p className="mt-2 text-pretty leading-relaxed">{section.body}</p>
              </section>
            ))}
          </div>

          <div className="mt-10 rounded-2xl bg-secondary/40 border border-border p-6">
            <h2 className="font-display text-xl font-semibold">Popular themes for gifts</h2>
            <div className="mt-4 flex flex-wrap gap-2">
              <Link
                to="/themes/$slug"
                params={{ slug: "dinosaurs" }}
                className="rounded-full border border-border bg-card px-4 py-2 text-sm font-medium hover:bg-secondary transition"
              >
                Dinosaurs
              </Link>
              <Link
                to="/themes/$slug"
                params={{ slug: "space" }}
                className="rounded-full border border-border bg-card px-4 py-2 text-sm font-medium hover:bg-secondary transition"
              >
                Space
              </Link>
              <Link
                to="/themes/$slug"
                params={{ slug: "pirates" }}
                className="rounded-full border border-border bg-card px-4 py-2 text-sm font-medium hover:bg-secondary transition"
              >
                Pirates
              </Link>
            </div>
          </div>

          <Link
            to="/"
            hash="generator"
            className="group inline-flex mt-10 items-center gap-2 rounded-full bg-primary text-primary-foreground px-6 py-3.5 font-semibold shadow-soft hover:translate-y-[-1px] transition"
          >
            Create a free preview
            <ArrowRight className="h-4 w-4 transition group-hover:translate-x-0.5" />
          </Link>
        </article>
      </main>
      <Footer />
    </div>
  );
}
