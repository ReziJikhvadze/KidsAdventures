export type BlogSection = {
  heading?: string;
  paragraphs: string[];
  bullets?: string[];
};

export type BlogPost = {
  slug: string;
  title: string;
  description: string;
  publishedAt: string;
  readingTimeMinutes: number;
  intro: string;
  sections: BlogSection[];
};

export const personalizedDinosaurGift: BlogPost = {
  slug: "personalized-dinosaur-gift",
  title: "Why a personalized dinosaur book is the perfect kids gift",
  description:
    "A custom dinosaur storybook starring your child beats generic toys — free preview, printable PDF, and gift ideas for birthdays and grandparents.",
  publishedAt: "2026-06-02",
  readingTimeMinutes: 5,
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
        "Grandparents often want a meaningful gift without guessing toy sizes. Email the PDF or print it at home and wrap it with a note: \"You are the star of this adventure.\"",
        "Explore our dinosaur theme page or gift guide for more ideas.",
      ],
    },
  ],
};
