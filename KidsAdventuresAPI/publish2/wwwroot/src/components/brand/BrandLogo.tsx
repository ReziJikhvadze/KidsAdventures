import { BookOpen } from "lucide-react";
import { Link } from "@tanstack/react-router";

type BrandLogoProps = {
  className?: string;
  asLink?: boolean;
};

export function BrandLogo({ className = "", asLink = true }: BrandLogoProps) {
  const content = (
    <>
      <span className="inline-flex items-center justify-center h-8 w-8 rounded-xl bg-primary text-primary-foreground shrink-0">
        <BookOpen className="h-4 w-4" />
      </span>
      <span className="font-display text-lg font-bold tracking-tight">LittleHero Books</span>
    </>
  );

  if (!asLink) {
    return <div className={`flex items-center gap-2 ${className}`}>{content}</div>;
  }

  return (
    <Link to="/" className={`flex items-center gap-2 ${className}`}>
      {content}
    </Link>
  );
}
