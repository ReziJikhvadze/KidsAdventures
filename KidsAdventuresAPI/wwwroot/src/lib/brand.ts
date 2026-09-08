import brandLogoUrl from "@/assets/adventrya_logo.png";
import brandMarkColorUrl from "@/assets/beki-logo-color.png";
import brandMarkWhiteUrl from "@/assets/beki-logo-white.png";

export const BRAND_NAME = "Beki";
/**
 * The header used to need a shorter label, because "Adventrya Books" did not fit its bar and was
 * cut to "Books" — a word that names the category rather than the product. "Beki" is already
 * short, so the header can say the name.
 */
export const BRAND_HEADER_NAME = "Beki";
export const BRAND_TAGLINE = "Personalized storybooks for kids";
export const BRAND_LOGO_URL = brandLogoUrl;

/*
  The lockup — the chameleon and the word — in its two finishes.

  Both are the supplied artwork with the empty pixels taken off: the exports sit on a square
  canvas with about 44% of its height blank above and below the mark, so an <img> given a
  header's worth of height would have drawn the mark at a third of it. The trimmed files are
  720x256, which is the aspect ratio every slot below sizes by.

  White for a dark bar, colour for a light one. Only one bar on the site is light — the parent's
  space, once they are signed in — so `BEKI_MARK_WHITE_URL` is what almost every page wants.
*/
export const BEKI_MARK_WHITE_URL = brandMarkWhiteUrl;
export const BEKI_MARK_COLOR_URL = brandMarkColorUrl;

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
