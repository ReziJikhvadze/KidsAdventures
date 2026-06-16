import { createFileRoute, Link } from "@tanstack/react-router";
import { ArrowRight, BookOpen, Check } from "lucide-react";

import { JsonLd } from "@/components/seo/JsonLd";
import { Nav } from "@/components/site/Nav";
import { Footer } from "@/components/site/Footer";
import { BRAND_NAME } from "@/lib/brand";
import { buildPageMeta } from "@/lib/seo";
import { buildBreadcrumbSchema, buildWebPageSchema } from "@/lib/structured-data";

const PAGE_TITLE = "Personalized Children's Books — Custom Storybooks Starring Your Child";
const PAGE_DESCRIPTION =
  "Create personalized children's books with your child's name, age, and photo. Illustrated adventure storybooks for kids ages 3–12 — free preview, printable PDF, screen-free parenting win.";

const HIGHLIGHTS = [
  "Your child's name on every page",
  "Illustrated cartoon hero from an optional photo",
  "Dinosaur, space, pirate, animal & airplane adventures",
  "Free 2-page preview — full 6-page books with credits",
  "Print at home for bedtime, gifts & learning time",
  "Kid-safe stories filtered for ages 3–12",
];

const SECTIONS = [
  {
    heading: "What makes a great personalized children's book?",
    body: "The best custom kids books put your child at the center of the story — not as a sticker on a template, but woven into the plot with age-appropriate language. Parents search for personalized children's books, custom storybooks, and name books because kids engage more when they see themselves on the page. Adventrya Books generates illustrated adventures in minutes.",
  },
  {
    heading: "Adventure books kids want to read again",
    body: "Generic child books sit on the shelf. Adventure books — dinosaurs, rockets, treasure hunts — invite re-reading and pretend play. Our themes are built for repeat storytime: act out scenes, draw the next chapter, or swap books between siblings. That is why families compare us to photo books and subscription boxes but keep coming back for printable PDFs they own forever.",
  },
  {
    heading: "A parenting-friendly gift for grandparents & holidays",
    body: "Grandparents love personalized children's books because they feel thoughtful without guessing toy sizes. Email a PDF, print and wrap it, or read aloud on video call. Book packs (3, 5, or 15 credits) never expire — ideal for birthdays, Christmas, Easter, and back-to-school surprises.",
  },
];

export const Route = createFileRoute("/personalized-childrens-books")({
  head: () => {
    const { meta, links } = buildPageMeta({
      title: `${PAGE_TITLE} | ${BRAND_NAME}`,
      description: PAGE_DESCRIPTION,
      path: "/personalized-childrens-books",
    });
    return { meta, links };
  },
  component: PersonalizedChildrensBooksPage,
});

function PersonalizedChildrensBooksPage() {
  return (
    <div className="min-h-screen bg-background text-foreground">
      <JsonLd
        data={[
          buildWebPageSchema({
            path: "/personalized-childrens-books",
            title: PAGE_TITLE,
            description: PAGE_DESCRIPTION,
          }),
          buildBreadcrumbSchema([
            { name: "Home", path: "/" },
            { name: "Personalized children's books", path: "/personalized-childrens-books" },
          ]),
        ]}
      />
      <Nav />
      <main className="mx-auto max-w-4xl px-6 py-16 md:py-24">
        <div className="inline-flex items-center gap-2 rounded-full border border-border bg-card px-3 py-1 text-xs font-medium text-muted-foreground">
          <BookOpen className="h-3.5 w-3.5" />
          Custom kids storybooks
        </div>
        <h1 className="mt-5 font-display text-4xl md:text-5xl font-bold text-balance">
          Personalized children's books your kid will actually finish
        </h1>
        <p className="mt-5 text-lg text-muted-foreground max-w-3xl">
          {PAGE_DESCRIPTION}
        </p>

        <ul className="mt-10 grid sm:grid-cols-2 gap-3">
          {HIGHLIGHTS.map((item) => (
            <li key={item} className="flex items-start gap-3 text-sm">
              <span className="mt-0.5 grid h-5 w-5 place-items-center rounded-full bg-primary/10 text-primary">
                <Check className="h-3 w-3" />
              </span>
              {item}
            </li>
          ))}
        </ul>

        <div className="mt-12 space-y-10">
          {SECTIONS.map((section) => (
            <section key={section.heading}>
              <h2 className="font-display text-2xl font-bold">{section.heading}</h2>
              <p className="mt-3 text-muted-foreground leading-relaxed">{section.body}</p>
            </section>
          ))}
        </div>

        <div className="mt-14 flex flex-wrap gap-3">
          <Link
            to="/"
            hash="generator"
            className="inline-flex items-center gap-2 rounded-full bg-primary text-primary-foreground px-6 py-3 font-semibold hover:opacity-90 transition"
          >
            Create your child's book
            <ArrowRight className="h-4 w-4" />
          </Link>
          <Link
            to="/themes"
            className="inline-flex items-center gap-2 rounded-full border border-border bg-card px-6 py-3 font-semibold hover:bg-secondary transition"
          >
            Browse adventure themes
          </Link>
        </div>
      </main>
      <Footer />
    </div>
  );
}
