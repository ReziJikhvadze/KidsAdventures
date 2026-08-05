import type { LegalSection } from "@/components/legal/LegalDocument";
import { BRAND_NAME } from "@/lib/brand";
import { LEGAL_CONTACT_EMAIL, LEGAL_WEBSITE } from "@/lib/legal";

export const privacyIntro = `This Privacy Policy explains how ${BRAND_NAME} (“we”, “us”) collects, uses, and protects personal data when you use ${LEGAL_WEBSITE}. We process data lawfully and transparently, including for users in Georgia, the EU/EEA, and other regions with similar privacy laws.`;

export const privacySections: LegalSection[] = [
  {
    id: "controller",
    title: "1. Who we are",
    paragraphs: [
      `${BRAND_NAME} operates the personalized storybook service at ${LEGAL_WEBSITE}.`,
      `For privacy questions or requests, contact us at [Contact us](/contact) or ${LEGAL_CONTACT_EMAIL}.`,
    ],
  },
  {
    id: "collect",
    title: "2. What data we collect",
    paragraphs: ["We may collect the following categories of data:"],
    bullets: [
      "Account data: email address, password (stored as a secure hash), account creation date, subscription/credit balance.",
      "Story inputs: child's first name, age, chosen theme, optional story wishes/notes, and story language.",
      "Optional photos: hero photos you upload (and family member photos if that feature is enabled). Photos may be processed to create cartoon-style illustrations.",
      "Generated content: story text, illustration files, PDF exports, and related metadata stored with your account.",
      "Payment data: purchases are handled by Stripe; we receive transaction references and credit fulfillment status, not your full card details.",
      "Technical data: IP address, browser/device type, server logs, and security/abuse-prevention records.",
      "Communications: messages you send through our contact form.",
    ],
  },
  {
    id: "photos",
    title: "3. Photos — important",
    paragraphs: [
      "If you upload a hero photo, it is stored in our secure cloud storage (Microsoft Azure Blob Storage) and linked to your account so we can generate and display illustrations consistently.",
      "Photos and related prompts are also sent to our AI provider (OpenAI) to generate story text and images. OpenAI processes this data on our behalf to produce output.",
      "We do not sell photos or personal data. We do not use your photos for unrelated advertising.",
      "Because photos may identify a child, we treat them as sensitive personal data and expect them to be uploaded only by a parent or legal guardian with appropriate authority.",
    ],
  },
  {
    id: "purpose",
    title: "4. Why we use your data",
    paragraphs: ["We use personal data to:"],
    bullets: [
      "Create and deliver personalized AI storybooks and PDFs you request.",
      "Authenticate your account and manage book credits.",
      "Send service emails (such as email confirmation, story-ready notifications, and support replies).",
      "Process payments and prevent fraud or abuse.",
      "Maintain, secure, and improve the platform (including troubleshooting and analytics from server logs).",
      "Comply with legal obligations.",
    ],
    afterBullets: ["We do not sell your personal data."],
  },
  {
    id: "legal-basis",
    title: "5. Legal basis (GDPR / similar laws)",
    paragraphs: ["Where GDPR or similar laws apply, we rely on:"],
    bullets: [
      "Contract — to provide the service you sign up for (account, story generation, PDF export).",
      "Consent — where required (for example optional photo upload and marketing emails if we offer them in future).",
      "Legitimate interests — security, fraud prevention, and improving the service, balanced against your rights.",
      "Legal obligation — where we must retain or disclose data under law.",
    ],
  },
  {
    id: "third-parties",
    title: "6. Third-party processors",
    paragraphs: [
      "We use trusted providers to run the service. They process data only as needed to perform their role:",
    ],
    bullets: [
      "OpenAI — AI story and image generation (prompts and optional reference photos).",
      "Microsoft Azure — cloud hosting, database (Azure SQL), and file storage (Blob Storage).",
      "Stripe — payment processing for book credit purchases.",
      "Email provider (SMTP) — transactional emails such as account confirmation and notifications.",
    ],
    afterBullets: [
      "Each provider has its own privacy and security practices. We choose providers that offer appropriate safeguards for personal data.",
    ],
  },
  {
    id: "children",
    title: "7. Children's data",
    paragraphs: [
      `${BRAND_NAME} is not directed at children under 13 to register or use the service on their own.`,
      "Children's information (name, age, preferences, optional photos) should be entered only by a parent or legal guardian.",
      "If you believe we have collected a child's data without proper parental consent, contact us immediately and we will take appropriate steps to delete it.",
    ],
  },
  {
    id: "retention",
    title: "8. How long we keep data",
    paragraphs: [
      "We retain data for as long as your account is active and as needed to provide the service, unless you request deletion or we are required to retain data longer by law.",
      "Typical retention:",
    ],
    bullets: [
      "Account and story library data — until you delete your account or ask us to delete it.",
      "Uploaded photos and generated illustrations — stored with your stories until account/content deletion.",
      "Server and security logs — usually up to 90 days, unless needed longer for investigation.",
      "Payment records — as required for accounting, tax, and dispute resolution (often several years, depending on law).",
    ],
  },
  {
    id: "security",
    title: "9. Security",
    paragraphs: [
      "We use HTTPS encryption in transit. Data at rest is stored on secured cloud infrastructure with access controls.",
      "Passwords are hashed; we do not store plain-text passwords.",
      "No method of transmission or storage is 100% secure. Please use a strong, unique password and keep it confidential.",
    ],
  },
  {
    id: "rights",
    title: "10. Your rights",
    paragraphs: [
      "Depending on your location, you may have the right to access, correct, delete, restrict, or object to certain processing of your personal data, and to data portability.",
      "You may also withdraw consent where processing is based on consent, without affecting prior lawful processing.",
      "To exercise these rights, contact us at [Contact us](/contact) or " +
        LEGAL_CONTACT_EMAIL +
        ". We may need to verify your identity.",
      "If you are in the EU/EEA, you may lodge a complaint with your local data protection authority.",
    ],
  },
  {
    id: "transfers",
    title: "11. International transfers",
    paragraphs: [
      "Our service providers may process data in countries outside your own (including the United States). Where required, we use appropriate safeguards such as standard contractual clauses or equivalent mechanisms.",
    ],
  },
  {
    id: "cookies",
    title: "12. Cookies and local storage",
    paragraphs: [
      "We use essential cookies/local storage for sign-in (authentication token) and core site functionality. We do not use invasive third-party advertising cookies on the storybook app today.",
      "If we add analytics or marketing cookies in future, we will update this policy and, where required, ask for consent.",
    ],
  },
  {
    id: "changes",
    title: "13. Changes to this policy",
    paragraphs: [
      "We may update this Privacy Policy from time to time. The “Last updated” date at the top will change when we do.",
      "Material changes may be communicated via the website or email where appropriate.",
    ],
  },
  {
    id: "contact",
    title: "14. Contact",
    paragraphs: [
      `Privacy questions or data requests: [Contact us](/contact) or ${LEGAL_CONTACT_EMAIL}.`,
    ],
  },
];
