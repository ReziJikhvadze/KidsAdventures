import {
  Accordion,
  AccordionContent,
  AccordionItem,
  AccordionTrigger,
} from "@/components/ui/accordion";
import { FAQ_ITEMS } from "@/lib/faq";

export function FAQ() {
  return (
    <section id="faq" className="relative py-24 md:py-32 bg-secondary/40">
      <div className="mx-auto max-w-3xl px-6">
        <div className="text-center">
          <p className="text-sm font-semibold text-primary tracking-wide uppercase">FAQ</p>
          <h2 className="mt-3 font-display text-4xl md:text-5xl font-bold text-balance">
            Questions parents ask before making a book.
          </h2>
        </div>

        <Accordion type="single" collapsible className="mt-12 space-y-3">
          {FAQ_ITEMS.map((f, i) => (
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
