import { createFileRoute, Link } from "@tanstack/react-router";
import { ArrowRight, GraduationCap, Check } from "lucide-react";

import { JsonLd } from "@/components/seo/JsonLd";
import { Nav } from "@/components/site/Nav";
import { Footer } from "@/components/site/Footer";
import { BRAND_NAME } from "@/lib/brand";
import { buildPageMeta } from "@/lib/seo";
import { buildBreadcrumbSchema, buildWebPageSchema } from "@/lib/structured-data";

const PAGE_TITLE = "Kids Learning Through Stories — Child Education & Reading at Home";
const PAGE_DESCRIPTION =
  "Support child education with personalized adventure books. Screen-free learning, early literacy, vocabulary, and parenting-friendly read-aloud stories for kids.";

const LEARNING_BENEFITS = [
  "Age-adapted vocabulary for early readers and older kids",
  "Read-aloud practice for listening & comprehension",
  "Problem-solving plots that spark conversation",
  "Screen-free learning parents trust for bedtime & weekends",
  "Printable books for classrooms, homeschool & travel",
  "Themes kids love: space (STEM), animals, dinosaurs & more",
];

const SECTIONS = [
  {
    heading: "Child education through personalized stories",
    body: "Research-backed parenting advice keeps coming back to one habit: reading together. Personalized stories increase engagement because children hear their own name, face age-appropriate challenges, and care about the outcome. That makes Adventrya Books a practical tool for child education at home — not a replacement for school, but a supplement that feels like play.",
  },
  {
    heading: "Learning adventures vs. passive screen time",
    body: "Parents searching for educational activities often land on apps first. Illustrated adventure books offer a different path: hold the pages, point at pictures, predict what happens next, and retell the story from memory. Space missions teach curiosity; animal tales build empathy; pirate quests encourage planning. It is learning through narrative — the oldest parenting tool, made personal.",
  },
  {
    heading: "For teachers, homeschoolers & caregivers",
    body: "Print multiple copies from a book pack for small groups, or assign each child their own theme. Stories are kid-safe and filtered for appropriate language. Use the free preview to test a theme before buying credits for full 6-page illustrated PDFs.",
  },
];

export const Route = createFileRoute("/kids-learning-books")({
  head: () => {
    const { meta, links } = buildPageMeta({
      title: `${PAGE_TITLE} | ${BRAND_NAME}`,
      description: PAGE_DESCRIPTION,
      path: "/kids-learning-books",
    });
    return { meta, links };
  },
  component: KidsLearningBooksPage,
});

function KidsLearningBooksPage() {
  return (
    <div className="min-h-screen bg-background text-foreground">
      <JsonLd
        data={[
          buildWebPageSchema({
            path: "/kids-learning-books",
            title: PAGE_TITLE,
            description: PAGE_DESCRIPTION,
          }),
          buildBreadcrumbSchema([
            { name: "Home", path: "/" },
            { name: "Kids learning books", path: "/kids-learning-books" },
          ]),
        ]}
      />
      <Nav />
      <main className="mx-auto max-w-4xl px-6 py-16 md:py-24">
        <div className="inline-flex items-center gap-2 rounded-full border border-border bg-card px-3 py-1 text-xs font-medium text-muted-foreground">
          <GraduationCap className="h-3.5 w-3.5" />
          Child education & parenting
        </div>
        <h1 className="mt-5 font-display text-4xl md:text-5xl font-bold text-balance">
          Kids learning through stories — education that feels like adventure
        </h1>
        <p className="mt-5 text-lg text-muted-foreground max-w-3xl">
          {PAGE_DESCRIPTION}
        </p>

        <ul className="mt-10 grid sm:grid-cols-2 gap-3">
          {LEARNING_BENEFITS.map((item) => (
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
            Start a learning adventure
            <ArrowRight className="h-4 w-4" />
          </Link>
          <Link
            to="/blog"
            className="inline-flex items-center gap-2 rounded-full border border-border bg-card px-6 py-3 font-semibold hover:bg-secondary transition"
          >
            Parenting & reading tips
          </Link>
        </div>
      </main>
      <Footer />
    </div>
  );
}
