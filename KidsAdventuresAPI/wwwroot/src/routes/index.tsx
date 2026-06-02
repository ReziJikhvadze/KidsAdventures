import { createFileRoute } from "@tanstack/react-router";
import { Nav } from "@/components/site/Nav";
import { Hero } from "@/components/site/Hero";
import { HowItWorks } from "@/components/site/HowItWorks";
import { Themes } from "@/components/site/Themes";
import { Preview } from "@/components/site/Preview";
import { Benefits } from "@/components/site/Benefits";
import { Pricing } from "@/components/site/Pricing";
import { FAQ } from "@/components/site/FAQ";
import { Generator } from "@/components/site/Generator";
import { Grandparents } from "@/components/site/Grandparents";
import { FinalCTA } from "@/components/site/FinalCTA";
import { Footer } from "@/components/site/Footer";

export const Route = createFileRoute("/")({
  head: () => ({
    meta: [
      { title: "AdventurePacks — Personalized printable adventures for kids" },
      {
        name: "description",
        content:
          "Generate personalized printable adventure books for kids ages 4–12 in under 60 seconds. Stories, puzzles, activities and certificates.",
      },
      {
        property: "og:title",
        content: "AdventurePacks — Personalized printable adventures for kids",
      },
      {
        property: "og:description",
        content:
          "Custom stories, puzzles and printable activities personalized for your child in under 60 seconds.",
      },
    ],
  }),
  component: Landing,
});

function Landing() {
  return (
    <div className="min-h-screen bg-background text-foreground">
      <Nav />
      <main>
        <Hero />
        <HowItWorks />
        <Generator />
        <Themes />
        <Preview />
        <Benefits />
        <Grandparents />
        <Pricing />
        <FAQ />
        <FinalCTA />
      </main>
      <Footer />
    </div>
  );
}
