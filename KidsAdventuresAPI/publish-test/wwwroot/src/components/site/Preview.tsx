import { Lock, Sparkles } from "lucide-react";

import { StoryBookReader } from "@/components/story/StoryBookReader";

const demoPages = [
  {
    title: "The Skyward Quest Begins",
    content:
      "When Leo looked out the window, the clouds had shaped themselves into airplanes — and one of them had his name painted on the side in golden letters.",
    isIllustrated: true,
    illustrationUrl: "/demo/demo-page-1.png",
  },
  {
    title: "Through the Cloud Kingdom",
    content:
      "The friendly captain handed Leo a map made of starlight. Every page of your book can look like this — with a unique illustration starring your child.",
    isIllustrated: true,
    illustrationUrl: "/demo/demo-page-2.png",
  },
  {
    title: "A Hero's Landing",
    content:
      "Leo landed softly on a runway of rainbow light. Mom and Dad cheered from the observation deck as the adventure came to a happy end.",
    isIllustrated: true,
    illustrationUrl: "/demo/demo-page-3.png",
  },
];

export function Preview() {
  return (
    <section id="preview" className="relative py-24 md:py-32">
      <div className="mx-auto max-w-7xl px-6">
        <div className="grid lg:grid-cols-2 gap-12 items-start">
          <div className="max-w-xl">
            <p className="text-sm font-semibold text-primary tracking-wide uppercase">Preview</p>
            <h2 className="mt-3 font-display text-4xl md:text-5xl font-bold text-balance">
              Read like a real picture book.
            </h2>
            <p className="mt-4 text-muted-foreground text-pretty">
              Every story is a 6-page picture book with warm, animated-style illustrations on every
              page. PDF export is free — book credits unlock extra stories.
            </p>
            <ul className="mt-6 space-y-3 text-sm">
              <li className="flex items-start gap-3">
                <Sparkles className="h-4 w-4 text-primary mt-0.5 shrink-0" />
                <span>
                  <strong className="text-foreground">Illustrated slideshow free</strong> — swipe
                  through every painted page before you buy.
                </span>
              </li>
              <li className="flex items-start gap-3">
                <Lock className="h-4 w-4 text-muted-foreground mt-0.5 shrink-0" />
                <span>
                  <strong className="text-foreground">PDF export is free</strong> — download, print,
                  and share anytime.
                </span>
              </li>
              <li className="flex items-start gap-3">
                <span className="text-primary font-display font-bold mt-0.5">Aa</span>
                <span>Adjustable text size for bedtime read-aloud on phone or tablet.</span>
              </li>
            </ul>
          </div>

          <div className="relative">
            <div
              className="absolute -inset-4 rounded-3xl opacity-60 pointer-events-none"
              style={{ background: "color-mix(in oklab, var(--sky-soft) 40%, transparent)" }}
            />
            <div className="relative rounded-3xl border border-border bg-card p-4 sm:p-6 shadow-card">
              <p className="text-xs font-semibold text-center text-muted-foreground mb-4 uppercase tracking-wide">
                Demo · swipe the pages · try fullscreen
              </p>
              <StoryBookReader
                pages={demoPages}
                theme="Airplanes"
                title="Leo's Sky Adventure"
                childName="Leo"
                previewIllustrationStatus="Ready"
                isCompleted={false}
              />
              <p className="mt-4 text-center text-xs text-muted-foreground">
                Sample story with demo art — your book uses your child&apos;s photo and name.
              </p>
            </div>
          </div>
        </div>
      </div>
    </section>
  );
}
