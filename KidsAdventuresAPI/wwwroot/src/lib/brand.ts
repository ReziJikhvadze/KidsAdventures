import brandLogoUrl from "@/assets/adventrya_logo.png";

export const BRAND_NAME = "Adventrya Books";
/** Shorter label for the cramped header bar */
export const BRAND_HEADER_NAME = "Books";
export const BRAND_TAGLINE = "Personalized storybooks for kids";
export const BRAND_LOGO_URL = brandLogoUrl;

export type BrandSocialLink = {
  label: string;
  href: string;
};

export const BRAND_SOCIAL_LINKS: BrandSocialLink[] = [
  {
    label: "TikTok",
    href: "https://www.tiktok.com/@adventrya.books",
  },
  {
    label: "Pinterest",
    href: "https://www.pinterest.com/rezijikhvadze/",
  },
  {
    label: "Facebook",
    href: "https://www.facebook.com/profile.php?id=61590674259707",
  },
  {
    label: "LinkedIn",
    href: "https://www.linkedin.com/company/adventrya",
  },
];
