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
      { title: "LittleHero Books — Personalized storybooks for kids" },
      {
        name: "description",
        content:
          "Create personalized illustrated storybooks starring your child. Story in minutes, PDF when you are ready.",
      },
      {
        property: "og:title",
        content: "LittleHero Books — Personalized storybooks for kids",
      },
      {
        property: "og:description",
        content:
          "Personalized stories and optional illustrated PDFs for children ages 3–12.",
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
