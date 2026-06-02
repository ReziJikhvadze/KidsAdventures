import {
  Accordion,
  AccordionContent,
  AccordionItem,
  AccordionTrigger,
} from "@/components/ui/accordion";

const faqs = [
  {
    q: "How is each adventure pack personalized?",
    a: "We weave your child's name and age into the story, puzzles and certificate, and tailor difficulty to their age group (4–12).",
  },
  {
    q: "How long does it take to generate a pack?",
    a: "Most adventure packs are ready in under 60 seconds. You'll get a print-ready PDF straight away.",
  },
  {
    q: "What do I need to print at home?",
    a: "Any standard color printer and A4 or US Letter paper. Coloring pages also work great in black and white.",
  },
  {
    q: "Is the content kid-safe?",
    a: "Yes. All stories are reviewed by our editorial team and filtered for age-appropriate language and themes.",
  },
  {
    q: "Can I cancel Premium anytime?",
    a: "Absolutely. Cancel in one click from your account — no questions asked.",
  },
  {
    q: "Do you offer refunds?",
    a: "If you're not happy within the first 14 days of Premium, we'll refund you in full.",
  },
];

export function FAQ() {
  return (
    <section id="faq" className="relative py-24 md:py-32 bg-secondary/40">
      <div className="mx-auto max-w-3xl px-6">
        <div className="text-center">
          <p className="text-sm font-semibold text-primary tracking-wide uppercase">FAQ</p>
          <h2 className="mt-3 font-display text-4xl md:text-5xl font-bold text-balance">
            Questions, answered.
          </h2>
        </div>

        <Accordion type="single" collapsible className="mt-12 space-y-3">
          {faqs.map((f, i) => (
            <AccordionItem
              key={i}
              value={`item-${i}`}
              className="rounded-2xl bg-card border border-border px-5 shadow-soft"
            >
              <AccordionTrigger className="text-left font-display text-lg font-semibold hover:no-underline">
                {f.q}
              </AccordionTrigger>
              <AccordionContent className="text-muted-foreground">{f.a}</AccordionContent>
            </AccordionItem>
          ))}
        </Accordion>
      </div>
    </section>
  );
}
