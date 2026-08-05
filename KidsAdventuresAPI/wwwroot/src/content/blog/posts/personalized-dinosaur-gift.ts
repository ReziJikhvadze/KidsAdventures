import dinosaurCover from "@/assets/theme-dinosaurs.jpg";

export type BlogSection = {
  heading?: string;
  paragraphs: string[];
  bullets?: string[];
};

export type BlogFaq = {
  q: string;
  a: string;
};

export type BlogPost = {
  slug: string;
  title: string;
  description: string;
  publishedAt: string;
  /** Last meaningful edit — drives schema dateModified and the visible "Updated" line. Defaults to publishedAt. */
  updatedAt?: string;
  readingTimeMinutes: number;
  intro: string;
  sections: BlogSection[];
  /** E-E-A-T byline — references an author in `content/blog/authors.ts`. Defaults to the editorial team. */
  authorId?: string;
  /** Optional FAQ block — renders a visible list and a FAQPage schema. */
  faqs?: BlogFaq[];
  /** Optional keyword hints surfaced in article schema. */
  keywords?: string[];
  /** Above-the-fold cover image (Vite-resolved URL). Feeds LCP preload, og:image, and BlogPosting.image. */
  coverImage?: string;
  /** Descriptive, keyword-aware alt text for the cover image. */
  coverImageAlt?: string;
};

export const personalizedDinosaurGift: BlogPost = {
  slug: "personalized-dinosaur-gift",
  title: "Why a personalized dinosaur book is the perfect kids gift",
  description:
    "A custom dinosaur storybook starring your child beats generic toys — free preview, printable PDF, and gift ideas for birthdays and grandparents.",
  publishedAt: "2026-06-02",
  updatedAt: "2026-06-24",
  readingTimeMinutes: 5,
  authorId: "rezi",
  coverImage: dinosaurCover,
  coverImageAlt:
    "Personalized dinosaur adventure storybook for kids, with a child as the hero exploring a prehistoric jungle",
  keywords: [
    "personalized dinosaur book",
    "custom dinosaur storybook",
    "dinosaur gift for kids",
    "personalized kids book gift",
  ],
  intro:
    "If you are shopping for a dinosaur-obsessed child, a personalized storybook beats another plastic figure. Here is why parents and grandparents choose Adventrya Books.",
  sections: [
    {
      heading: "It feels made just for them",
      paragraphs: [
        "Generic gifts are forgotten quickly. A story with your child's name on every page — plus optional cartoon illustrations from their photo — becomes a keepsake they ask to read again.",
        "Our Dinosaur theme sends young explorers through jungles and fossil valleys where they are the hero. You can add optional wishes too: a favorite dino, a sibling, or a birthday message woven into the plot.",
      ],
    },
    {
      heading: "Try before you buy",
      paragraphs: [
        "Every new account gets a free 2-page welcome preview. Read the slideshow in the browser before spending a cent. When you love it, book credits unlock the full 6-page illustrated PDF.",
      ],
      bullets: [
        "Free 2-page preview — no card required",
        "Full books use one credit each",
        "Credits never expire",
      ],
    },
    {
      heading: "Perfect for grandparents",
      paragraphs: [
        'Grandparents often want a meaningful gift without guessing toy sizes. Email the PDF or print it at home and wrap it with a note: "You are the star of this adventure."',
        "Explore our dinosaur theme page or gift guide for more ideas.",
      ],
    },
  ],
  faqs: [
    {
      q: "How much does a personalized dinosaur book cost?",
      a: "The full dinosaur story is free to create and read. Your first book includes one free illustrated page, and a one-time $4.99 unlocks the complete illustrated storybook. The printable PDF download is always free.",
    },
    {
      q: "Can I add my child's photo to the dinosaur book?",
      a: "Yes. Upload an optional photo and we create a matching cartoon hero across the illustrated pages, so your child becomes the star of the dinosaur adventure.",
    },
    {
      q: "Is a personalized dinosaur book a good gift from grandparents?",
      a: "It is one of the most popular grandparent gifts — meaningful, screen-free, and easy to email or print. There are no toy sizes to guess and the keepsake lasts far longer than a plastic figure.",
    },
  ],
};
