import { BrandLogo } from "@/components/brand/BrandLogo";
import { BRAND_NAME } from "@/lib/brand";

export function Footer() {
  const cols = [
    { title: "Product", links: ["Themes", "Pricing", "Examples", "What's new"] },
    { title: "Company", links: ["About", "Blog", "Press", "Contact"] },
    { title: "Support", links: ["Help center", "Printing tips", "Privacy", "Terms"] },
  ];
  return (
    <footer className="border-t border-border bg-background">
      <div className="mx-auto max-w-7xl px-6 py-16 grid md:grid-cols-[1.5fr_1fr_1fr_1fr] gap-10">
        <div>
          <BrandLogo asLink={false} />
          <p className="mt-4 text-sm text-muted-foreground max-w-sm">
            Personalized printable adventure books for kids ages 4–12. Made with care by parents,
            for parents.
          </p>
        </div>
        {cols.map((c) => (
          <div key={c.title}>
            <div className="text-sm font-semibold">{c.title}</div>
            <ul className="mt-4 space-y-2 text-sm text-muted-foreground">
              {c.links.map((l) => (
                <li key={l}>
                  <a href="#" className="hover:text-foreground transition">
                    {l}
                  </a>
                </li>
              ))}
            </ul>
          </div>
        ))}
      </div>
      <div className="border-t border-border">
        <div className="mx-auto max-w-7xl px-6 py-6 flex flex-col sm:flex-row gap-3 justify-between items-center text-xs text-muted-foreground">
          <div>© {new Date().getFullYear()} {BRAND_NAME}. All rights reserved.</div>
          <div>Made with love for curious kids.</div>
        </div>
      </div>
    </footer>
  );
}
