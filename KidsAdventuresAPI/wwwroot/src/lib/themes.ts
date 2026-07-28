import airplanes from "@/assets/theme-airplanes.jpg";
import dinosaurs from "@/assets/theme-dinosaurs.jpg";
import space from "@/assets/theme-space.jpg";
import pirates from "@/assets/theme-pirates.jpg";
import animals from "@/assets/theme-animals.jpg";
import type { ThemeType } from "@/lib/api/types";

export type StoryThemeId =
  | "airplanes"
  | "dinosaurs"
  | "space"
  | "pirates"
  | "animals"
  | "magic";

export type StoryTheme = {
  id: StoryThemeId;
  slug: StoryThemeId;
  name: string;
  shortDesc: string;
  tint: string;
  image: string;
  apiTheme: ThemeType;
  seoTitle: string;
  seoDescription: string;
  heroHeading: string;
  intro: string;
  paragraphs: string[];
  highlights: string[];
};

export const STORY_THEMES: StoryTheme[] = [
  {
    id: "airplanes",
    slug: "airplanes",
    name: "Airplanes",
    shortDesc: "Take to the skies",
    tint: "var(--sky-soft)",
    image: airplanes,
    apiTheme: "Airplanes",
    seoTitle: "Personalized Airplane Adventure Book for Kids | Adventrya Books",
    seoDescription:
      "Create a custom illustrated airplane adventure book starring your child. Personalized children's books — free preview, printable PDF, screen-free parenting.",
    heroHeading: "Personalized airplane adventure book for kids",
    intro:
      "Send your child soaring through cloud kingdoms, friendly airports, and high-flying missions — with their name woven into every page.",
    paragraphs: [
      "Adventrya Books turns your child's name and age into a full illustrated adventure. Choose the Airplanes theme and we generate a kid-safe story with cartoon illustrations you can read online or export as a print-ready PDF.",
      "Upload an optional hero photo and we create a matching cartoon character across every illustrated page. Grandparents love this theme as a birthday or holiday gift because it feels personal without requiring a trip to the store.",
      "Start with a free 2-page welcome preview — no card required. When you are ready for the complete 6-page picture book, use book credits for a downloadable PDF you can print at home.",
    ],
    highlights: [
      "Kid-safe stories for every young reader",
      "Optional photo-personalized cartoon hero",
      "Printable PDF for home or classroom",
    ],
  },
  {
    id: "dinosaurs",
    slug: "dinosaurs",
    name: "Dinosaurs",
    shortDesc: "Roar into the past",
    tint: "var(--mint)",
    image: dinosaurs,
    apiTheme: "Dinosaurs",
    seoTitle: "Personalized Dinosaur Storybook for Kids | Adventrya Books",
    seoDescription:
      "Make a personalized dinosaur adventure book with your child as the hero. Custom kids storybooks for child education & fun — illustrated pages, free preview, printable PDF.",
    heroHeading: "Personalized dinosaur storybook for kids",
    intro:
      "Journey through jungles, fossil digs, and prehistoric valleys where your child meets friendly dinosaurs and saves the day.",
    paragraphs: [
      "Dinosaur-loving kids light up when they see their own name on a story page. Adventrya Books builds a personalized illustrated adventure around your child's age and optional wishes — perfect for birthdays, holidays, or rainy-day screen-free fun.",
      "Each book includes custom story text and optional cartoon illustrations based on an uploaded photo. The result is a keepsake PDF parents and grandparents can print and read together.",
      "Try the free 2-page preview first, then unlock the full 6-page illustrated book with book credits that never expire.",
    ],
    highlights: [
      "Great gift for dino fans",
      "Name on every page",
      "Free preview before you buy credits",
    ],
  },
  {
    id: "space",
    slug: "space",
    name: "Space",
    shortDesc: "Explore the stars",
    tint: "var(--accent)",
    image: space,
    apiTheme: "Space",
    seoTitle: "Personalized Space Adventure Book for Kids | Adventrya Books",
    seoDescription:
      "Launch a personalized space adventure book starring your child. STEM-friendly kids learning stories — illustrated missions, free preview, printable PDF.",
    heroHeading: "Personalized space adventure book for kids",
    intro:
      "Blast off to distant planets, asteroid fields, and star-filled missions where your child is the brave astronaut in charge.",
    paragraphs: [
      "The Space theme is ideal for curious kids who love rockets, planets, and big imagination. We personalize the adventure with your child's name, age, and optional story wishes.",
      "Add a hero photo to turn them into a consistent cartoon astronaut across illustrated pages. Stories are filtered for age-appropriate language and themes.",
      "Read the slideshow in your browser, then export a printable PDF when you are ready — one credit per illustrated book, with packs starting at three books.",
    ],
    highlights: [
      "STEM-friendly adventure stories",
      "Illustrated slideshow + PDF export",
      "Works for siblings with separate books",
    ],
  },
  {
    id: "pirates",
    slug: "pirates",
    name: "Pirates",
    shortDesc: "Hunt the treasure",
    tint: "var(--sun)",
    image: pirates,
    apiTheme: "Pirates",
    seoTitle: "Personalized Pirate Adventure Book for Kids | Adventrya Books",
    seoDescription:
      "Create a personalized pirate adventure book for your child. Illustrated kids storybooks for bedtime & gifts — free preview, printable PDF.",
    heroHeading: "Personalized pirate adventure book for kids",
    intro:
      "Sail across sparkling seas, decode treasure maps, and outsmart silly sea creatures — with your child captaining the crew.",
    paragraphs: [
      "Pirate adventures are a hit for playtime and bedtime alike. Adventrya Books crafts a personalized story with your child's name at the center of the quest.",
      "Optional photo upload turns your child into a cartoon captain or first mate on every illustrated page. Content stays kid-safe — fun and adventurous without scary violence.",
      "Parents print the PDF at home on A4 or US Letter paper. Classrooms and camps can use book packs for multiple children.",
    ],
    highlights: [
      "Treasure-hunt stories kids replay",
      "Print at home in minutes",
      "Book packs for families and classrooms",
    ],
  },
  {
    id: "animals",
    slug: "animals",
    name: "Animals",
    shortDesc: "Meet the wild",
    tint: "var(--sun)",
    image: animals,
    apiTheme: "Animals",
    seoTitle: "Personalized Animal Adventure Book for Kids | Adventrya Books",
    seoDescription:
      "Make a personalized animal adventure book starring your child. Gentle illustrated children's books for early learning — free preview, printable PDF.",
    heroHeading: "Personalized animal adventure book for kids",
    intro:
      "Explore jungles, savannas, and cozy forests where your child helps friendly animals solve problems and learn together.",
    paragraphs: [
      "The Animals theme suits younger readers and nature lovers. Stories adapt to your child's age so the vocabulary always feels just right.",
      "Upload a photo to create a cartoon hero who explores alongside pandas, lions, dolphins, and more. Every story is unique because you can add optional wishes — a favorite animal, a lesson, or a setting.",
      "Start free with a 2-page welcome preview. Full illustrated books use one credit each and stay in My Books forever.",
    ],
    highlights: [
      "Gentle stories for younger readers",
      "Customize with optional wishes",
      "Credits never expire",
    ],
  },
  {
    id: "magic",
    slug: "magic",
    name: "ჯადოსნური სამყარო",
    shortDesc: "შეეხე ჯადოს",
    tint: "var(--lavender)",
    image: "/adventrya/magic-story-v3.webp",
    apiTheme: "Magic",
    seoTitle: "პერსონალიზებული ჯადოსნური წიგნი ბავშვებისთვის | Adventrya",
    seoDescription:
      "შექმენი პერსონალიზებული ჯადოსნური თავგადასავალი, სადაც მთავარი გმირი შენი ბავშვია — ილუსტრირებული გვერდები, უფასო ნიმუში, ბეჭდური ვერსია.",
    heroHeading: "პერსონალიზებული ჯადოსნური წიგნი ბავშვებისთვის",
    intro:
      "გააღე კარი სინათლის ქალაქში, სადაც შენი ბავშვი ჯადოს იყენებს სიკეთისთვის და მეგობრებს ეხმარება.",
    paragraphs: [
      "ჯადოსნური სამყარო Adventrya-ს მეექვსე თავგადასავალია — რბილი მაგია, მეგობრობა და აღმოჩენა, ძალადობის გარეშე.",
      "დაამატე პორტრეტი და გმირი ბავშვს ჰგავს ყოველ გვერდზე. სრული წიგნი იქმნება გადახდის შემდეგ.",
    ],
    highlights: [
      "რბილი, უსაფრთხო ჯადო",
      "ასაკზე მორგებული ტექსტი",
      "გაგრძელება Adventure Map-ზე",
    ],
  },
];

export function getThemeBySlug(slug: string): StoryTheme | undefined {
  return STORY_THEMES.find((theme) => theme.slug === slug);
}

export function isStoryThemeId(value: string): value is StoryThemeId {
  return STORY_THEMES.some((theme) => theme.id === value);
}
