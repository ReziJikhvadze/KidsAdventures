import type { LegalSection } from "@/components/legal/LegalDocument";
import { BRAND_NAME } from "@/lib/brand";
import { LEGAL_CONTACT_EMAIL, LEGAL_WEBSITE } from "@/lib/legal";

export const termsIntro = `These Terms & Conditions govern your use of ${BRAND_NAME} at ${LEGAL_WEBSITE}. By creating an account, purchasing book credits, or using the service, you agree to these terms.`;

export const termsSections: LegalSection[] = [
  {
    id: "acceptance",
    title: "1. Acceptance of terms",
    paragraphs: [
      `By accessing or using ${BRAND_NAME}, you confirm that you have read, understood, and agree to these Terms & Conditions and our [Privacy Policy](/privacy). If you do not agree, do not use the service.`,
    ],
  },
  {
    id: "service",
    title: "2. What our service does",
    paragraphs: [
      `${BRAND_NAME} is an online platform that helps parents and guardians create personalized, AI-generated storybooks for children.`,
      "The service may include:",
    ],
    bullets: [
      "Generating fictional story text based on information you provide (such as a child's first name, age, theme, and optional wishes).",
      "Optional upload of a hero photo to create illustrated, cartoon-style story pages.",
      "Reading stories in the browser and exporting printable PDF storybooks when available.",
      "Purchasing book credits through our payment partner.",
    ],
    afterBullets: [
      "We do not provide medical, legal, or professional advice. Generated content is not a substitute for professional guidance.",
    ],
  },
  {
    id: "ai-disclaimer",
    title: "3. AI content disclaimer",
    paragraphs: [
      "Stories and illustrations are generated using artificial intelligence. They are fictional and for entertainment purposes only.",
      "We do not guarantee factual accuracy, educational suitability for every child, or that output will always match your expectations. AI may produce unexpected wording, imagery, or errors.",
      "You are responsible for reviewing content before sharing it with a child, especially for younger readers.",
    ],
  },
  {
    id: "responsibilities",
    title: "4. Your responsibilities",
    paragraphs: ["When using the service, you agree that you will:"],
    bullets: [
      "Use the service lawfully and only for personal, family-oriented storybook creation.",
      "Provide accurate account information and keep your password secure.",
      "Ensure you have the legal right and parental authority to upload any photo or personal information about a child or other person.",
      "Not upload illegal, abusive, hateful, sexually explicit, or otherwise harmful content.",
      "Not attempt to reverse engineer, scrape, overload, or misuse the platform.",
    ],
  },
  {
    id: "children",
    title: "5. Children and parental responsibility",
    paragraphs: [
      `${BRAND_NAME} is intended to be used by adults — parents, guardians, or other authorized caregivers — not by children directly.`,
      "Accounts must be created by an adult. You are responsible for all activity under your account and for any information you enter about a child (including name, age, preferences, and optional photos).",
      "By uploading a child's photo or details, you confirm that you are the parent or legal guardian (or have obtained appropriate consent) and that you authorize us to process that information as described in our Privacy Policy.",
    ],
  },
  {
    id: "ip",
    title: "6. Intellectual property",
    paragraphs: [
      "You retain ownership of content you submit (such as photos and text inputs), subject to the licenses below.",
      `We own the ${BRAND_NAME} platform, software, branding, and underlying systems.`,
      "Subject to these terms and any applicable payment requirements, we grant you a personal, non-exclusive, non-transferable license to use generated storybooks and PDFs for private, non-commercial family use (for example reading at home, printing for your household, or sharing with close family).",
      "You may not resell, publicly redistribute, or commercially exploit generated content or the service without our written permission.",
    ],
  },
  {
    id: "payments",
    title: "7. Payments and credits",
    paragraphs: [
      "Book credits and other paid features are processed by our payment provider (Stripe). Prices, credit packs, and availability may change.",
      "Except where required by applicable law, purchases of digital credits are generally non-refundable once credits are delivered or used. Contact us if you believe a charge was made in error.",
    ],
  },
  {
    id: "availability",
    title: "8. Service availability",
    paragraphs: [
      "We aim to keep the service available and reliable, but we do not guarantee uninterrupted access. Maintenance, third-party outages (including AI or cloud providers), or force majeure may cause delays or failures in story or PDF generation.",
    ],
  },
  {
    id: "liability",
    title: "9. Limitation of liability",
    paragraphs: [
      `To the fullest extent permitted by law, ${BRAND_NAME} and its operator are not liable for indirect, incidental, special, or consequential damages arising from your use of the service or reliance on AI-generated content.`,
      "Our total liability for any claim relating to the service is limited to the amount you paid us for the relevant book credits in the twelve (12) months before the claim, or zero if you paid nothing.",
      "Nothing in these terms limits liability that cannot be excluded under applicable law (including certain consumer rights in the EU/EEA).",
    ],
  },
  {
    id: "termination",
    title: "10. Suspension and termination",
    paragraphs: [
      "We may suspend or terminate access if you breach these terms, misuse the service, or if required for legal or security reasons.",
      "You may stop using the service at any time. To request account or data deletion, contact us (see Privacy Policy).",
    ],
  },
  {
    id: "changes",
    title: "11. Changes to these terms",
    paragraphs: [
      "We may update these Terms & Conditions from time to time. The “Last updated” date at the top will change when we do.",
      "Continued use of the service after changes take effect means you accept the updated terms. For material changes, we may provide additional notice where appropriate.",
    ],
  },
  {
    id: "contact",
    title: "12. Contact",
    paragraphs: [
      `Questions about these terms: [Contact us](/contact) or email ${LEGAL_CONTACT_EMAIL}.`,
    ],
  },
];
