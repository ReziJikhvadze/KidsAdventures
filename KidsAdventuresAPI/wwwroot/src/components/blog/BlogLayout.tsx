import type { ReactNode } from "react";

import { Nav } from "@/components/site/Nav";
import { Footer } from "@/components/site/Footer";

type BlogLayoutProps = {
  children: ReactNode;
};

export function BlogLayout({ children }: BlogLayoutProps) {
  return (
    <div className="min-h-screen bg-background text-foreground">
      <Nav />
      <main className="pt-4">{children}</main>
      <Footer />
    </div>
  );
}
