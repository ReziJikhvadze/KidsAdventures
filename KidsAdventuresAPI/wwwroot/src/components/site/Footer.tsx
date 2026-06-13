import { Link } from "@tanstack/react-router";
import { BrandLogo } from "@/components/brand/BrandLogo";
import { BRAND_NAME } from "@/lib/brand";

const footerLinks: Record<string, { label: string; href: string }[]> = {
  Product: [
    { label: "Themes", href: "/#themes" },
    { label: "Pricing", href: "/#pricing" },
    { label: "Create a book", href: "/#generator" },
  ],
  Company: [{ label: "Contact", href: "/contact" }],
  Legal: [
    { label: "Terms & Conditions", href: "/terms" },
    { label: "Privacy Policy", href: "/privacy" },
  ],
  Support: [{ label: "FAQ", href: "/#faq" }],
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
                  {link.href.startsWith("/") && !link.href.includes("#") ? (
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
          <div className="flex flex-wrap items-center justify-center gap-x-3 gap-y-1">
            <Link to="/terms" className="hover:text-foreground transition">
              Terms
            </Link>
            <span aria-hidden="true">·</span>
            <Link to="/privacy" className="hover:text-foreground transition">
              Privacy
            </Link>
            <span aria-hidden="true">·</span>
            <Link to="/contact" className="hover:text-foreground transition">
              Contact
            </Link>
          </div>
        </div>
      </div>
    </footer>
  );
}
