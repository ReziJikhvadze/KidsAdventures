import brandLogoUrl from "@/assets/adventrya_logo.png";

export const BRAND_NAME = "Beki";
/**
 * The header used to need a shorter label, because "Adventrya Books" did not fit its bar and was
 * cut to "Books" — a word that names the category rather than the product. "Beki" is already
 * short, so the header can say the name.
 */
export const BRAND_HEADER_NAME = "Beki";
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
