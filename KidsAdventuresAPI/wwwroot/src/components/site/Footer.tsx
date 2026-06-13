import { Link } from "@tanstack/react-router";
import { BrandLogo } from "@/components/brand/BrandLogo";
import { BRAND_NAME } from "@/lib/brand";

const footerLinks: Record<string, { label: string; href: string; isRoute?: boolean }[]> = {
  Product: [
    { label: "Themes", href: "/#themes" },
    { label: "Gift guide", href: "/gift-guide", isRoute: true },
    { label: "Pricing", href: "/#pricing" },
    { label: "Create a book", href: "/#generator" },
  ],
  Resources: [
    { label: "Blog", href: "/blog", isRoute: true },
    { label: "Dinosaur books", href: "/themes/dinosaurs", isRoute: true },
    { label: "Space books", href: "/themes/space", isRoute: true },
    { label: "FAQ", href: "/#faq" },
  ],
  Company: [{ label: "Contact", href: "/contact", isRoute: true }],
  Legal: [
    { label: "Terms & Conditions", href: "/terms", isRoute: true },
    { label: "Privacy Policy", href: "/privacy", isRoute: true },
  ],
};

export function Footer() {
  return (
    <footer className="border-t border-border bg-background">
      <div className="mx-auto max-w-7xl px-6 py-16 grid md:grid-cols-[1.5fr_1fr_1fr_1fr_1fr] gap-10">
        <div>
          <BrandLogo asLink={false} />
          <p className="mt-4 text-sm text-muted-foreground max-w-sm">
            Personalized printable adventure books for kids ages 4–12. Made with care by parents,
            for parents.
          </p>
        </div>
        {Object.entries(footerLinks).map(([title, links]) => (
          <div key={title}>
            <div className="text-sm font-semibold">{title}</div>
            <ul className="mt-4 space-y-2 text-sm text-muted-foreground">
              {links.map((link) => (
                <li key={link.label}>
                  {link.isRoute ? (
                    <Link to={link.href} className="hover:text-foreground transition">
                      {link.label}
                    </Link>
                  ) : (
                    <a href={link.href} className="hover:text-foreground transition">
                      {link.label}
                    </a>
                  )}
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
