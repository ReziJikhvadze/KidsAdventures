import { Link } from "@tanstack/react-router";

const TOPICS = [
  {
    heading: "Personalized children's books & kids adventure stories",
    body: "Parents search for custom storybooks where their child is the hero — not a generic character. Beki creates illustrated adventure books with your child's name, age, and optional photo turned into a cartoon character. Choose dinosaur, space, pirate, animal, or airplane themes and get a printable PDF for bedtime, birthdays, or classroom gifts.",
    link: { label: "See all adventure themes", to: "/themes" as const },
  },
  {
    heading: "Child education & learning through reading",
    body: "Stories help children build vocabulary, empathy, and problem-solving skills without feeling like homework. Our age-adapted text supports early literacy, listening comprehension, and screen-free learning at home. Teachers and parents use personalized books for read-aloud time, rainy-day activities, and gentle bedtime routines.",
    link: { label: "Kids learning through stories", to: "/kids-learning-books" as const },
  },
  {
    heading: "Parenting: screen-free fun that actually works",
    body: "Modern parenting often means balancing tablets with meaningful offline time. A custom illustrated storybook gives kids something to hold, act out, and re-read — while grandparents and caregivers get an easy gift that feels personal. Start with a free 2-page preview, then unlock full 6-page books with credits that never expire.",
    link: {
      label: "Personalized children's books guide",
      to: "/personalized-childrens-books" as const,
    },
  },
];

export function SeoContent() {
  return (
    <section
      id="about-adventrya"
      className="relative py-20 md:py-28 border-t border-border bg-background"
      aria-labelledby="seo-content-heading"
    >
      <div className="mx-auto max-w-5xl px-6">
        <div className="max-w-3xl">
          <p className="text-sm font-semibold text-primary tracking-wide uppercase">
            For parents & educators
          </p>
          <h2
            id="seo-content-heading"
            className="mt-3 font-display text-3xl md:text-4xl font-bold text-balance"
          >
            Custom child books, adventures, and learning — in one place
          </h2>
          <p className="mt-4 text-muted-foreground text-lg">
            Whether you are looking for personalized children's books, educational storytime, or
            adventure tales starring your kid, Beki is built for families who want reading that
            feels made just for them.
          </p>
        </div>

        <div className="mt-12 grid gap-8 md:grid-cols-3">
          {TOPICS.map((topic) => (
            <article
              key={topic.heading}
              className="rounded-2xl border border-border bg-card p-6 shadow-soft"
            >
              <h3 className="font-display text-xl font-semibold text-balance">{topic.heading}</h3>
              <p className="mt-3 text-sm text-muted-foreground leading-relaxed">{topic.body}</p>
              <Link
                to={topic.link.to}
                className="mt-4 inline-block text-sm font-semibold text-primary hover:underline"
              >
                {topic.link.label} →
              </Link>
            </article>
          ))}
        </div>
      </div>
    </section>
  );
}
