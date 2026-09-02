import type { LegalSection } from "@/components/legal/LegalDocument";
import { BRAND_NAME } from "@/lib/brand";
import { LEGAL_CONTACT_EMAIL, LEGAL_WEBSITE } from "@/lib/legal";
import { MERCHANT } from "@/lib/merchant";

export const termsIntro = `These Terms & Conditions govern your use of ${BRAND_NAME} at ${LEGAL_WEBSITE}. By creating an account, purchasing a book, or using the service, you agree to these terms.`;

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
      "Purchasing a book through our payment provider.",
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
    title: "7. Payments",
    // Rewritten when Bank of Georgia replaced Stripe. The credit wallet it described had already
    // been replaced by per-book orders, so naming the new provider inside the old sentence would
    // have produced a paragraph that was accurate about nobody.
    paragraphs: [
      "Payments are processed by Bank of Georgia's online payment system. You enter your card details on the bank's own page; we receive the transaction result, never your full card number. Prices and availability may change.",
      "You buy a specific book rather than a balance of credits. Delivery times, cancellation and refunds are set out in our [Delivery and Refund Policy](/refunds).",
      "Except where required by applicable law, a personalized digital book is generally non-refundable once it has been generated, because it is made to your order and delivered immediately. Contact us if you believe a charge was made in error.",
      // The guarantee below is the exception to the sentence above, so it is named from here
      // rather than left several screens down for a customer to come across.
      "Where the fault is ours — a book that failed to generate, a double charge, a damaged or" +
        " wrong print — the guarantee in section 8 sets out what we do about it.",
    ],
  },
  /*
    The guarantee, in Georgian, in a document that is otherwise in English.

    Not a translation oversight: this is operative text a customer is entitled to rely on, and
    the customers are Georgian. Paraphrasing it into English to match the sections around it
    would change what was promised, which is the one thing a policy may not do. The rest of this
    document is the half that is out of step and is worth translating separately.

    Section 6 of the [Delivery and Refund Policy](/refunds) covers neighbouring ground; this is
    the stronger promise of the two — a decision inside five working days, a free remake, and a
    partial refund or voucher where a replacement would be too much — so the two are cross-linked
    rather than left to be found separately.
  */
  {
    id: "refund-guarantee",
    // The one Georgian section in an English document; the document itself declares `en`.
    lang: "ka",
    title: "8. თანხის დაბრუნებისა და ზიანის ანაზღაურების გარანტია",
    paragraphs: [
      `${BRAND_NAME}-სთვის მნიშვნელოვანია, რომ მიიღოთ ზუსტად ის პროდუქტი, რომელიც შეუკვეთეთ. თუ ჩვენი შეცდომის, ტექნიკური ხარვეზის, ბეჭდვის ან მიწოდების პრობლემის გამო პროდუქტი არ შეესაბამება შეკვეთას, ჩვენ განვიხილავთ შემთხვევას და შესაბამისი საფუძვლის არსებობისას შემოგთავაზებთ თანხის სრულ/ნაწილობრივ დაბრუნებას, პროდუქტის უფასოდ ხელახლა დამზადებას ან ჩანაცვლებას.`,
      "მომართვის დაფიქსირებიდან მაქსიმუმ 5 სამუშაო დღის განმავლობაში მოხდება შემთხვევის განხილვა და შესაბამისი გადაწყვეტილების მიღება/ანაზღაურების ინიცირება.",
    ],
    blocks: [
      {
        heading: "1. თანხის სრულად დაბრუნება",
        paragraphs: ["თანხა სრულად ანაზღაურდება, თუ:"],
        bullets: [
          "გადახდა განხორციელდა, მაგრამ ტექნიკური ხარვეზის გამო წიგნი ვერ შეიქმნა;",
          "მომხმარებელს თანხა შეცდომით ორჯერ ან მეტჯერ ჩამოეჭრა;",
          `შეკვეთა ${BRAND_NAME}-ს მიზეზით ვერ შესრულდა;`,
          "ბეჭდური წიგნის დამზადება ან მიწოდება შეუძლებელი გახდა;",
          "მიღებული პროდუქტი არსებითად განსხვავდება მომხმარებლის მიერ დადასტურებული შეკვეთისგან;",
          `დაფიქსირდა სხვა მნიშვნელოვანი ტექნიკური ან საოპერაციო შეცდომა ${BRAND_NAME}-ს მხრიდან, რის გამოც მომხმარებელმა ვერ მიიღო შეძენილი პროდუქტი.`,
        ],
      },
      {
        heading: "2. უფასო ჩანაცვლება / ხელახლა დამზადება",
        paragraphs: [
          `${BRAND_NAME} საკუთარი ხარჯით ხელახლა დაამზადებს ან ჩაანაცვლებს პროდუქტს, თუ:`,
        ],
        bullets: [
          "წიგნი დაზიანებულია;",
          "აკლია გვერდები;",
          "გვერდები არასწორი თანმიმდევრობითაა;",
          "ბეჭდვის ხარისხი მნიშვნელოვნად არის დარღვეული;",
          "წიგნი არასწორადაა აკინძული;",
          "მიღებულია სხვა მომხმარებლის წიგნი;",
          `ბავშვის სახელი, ფოტო ან სხვა პერსონალიზებული ინფორმაცია ${BRAND_NAME}-ს ტექნიკური შეცდომის გამო არასწორად აისახა;`,
          "ტრანსპორტირებისას პროდუქტი მნიშვნელოვნად დაზიანდა.",
        ],
        afterBullets: ["ასეთ შემთხვევაში ხელახალი დამზადება და მიწოდება მომხმარებლისთვის უფასოა."],
      },
      {
        heading: "3. ნაწილობრივი ანაზღაურება",
        paragraphs: [
          `თუ პრობლემა არ საჭიროებს პროდუქტის სრულ ჩანაცვლებას, ${BRAND_NAME}-მ მომხმარებელთან შეთანხმებით შეიძლება შესთავაზოს:`,
        ],
        bullets: [
          "გადახდილი თანხის ნაწილობრივი დაბრუნება;",
          "ფასდაკლება;",
          "შესაბამისი ღირებულების კრედიტი/ვაუჩერი მომდევნო შეკვეთისთვის.",
        ],
        afterBullets: ["არჩევანი მომხმარებელთან შეთანხმებით განხორციელდება."],
      },
      {
        heading: "4. ციფრული წიგნის შემთხვევები",
        paragraphs: [
          `თუ ციფრული წიგნი ტექნიკური მიზეზით არ იხსნება, არ იტვირთება, არასრულად შეიქმნა ან სისტემური შეცდომის გამო არსებითად არ შეესაბამება დადასტურებულ მონაცემებს, ${BRAND_NAME} პირველ რიგში შეეცდება პრობლემის გამოსწორებას ან წიგნის ხელახლა გენერირებას. თუ პრობლემის გამოსწორება შეუძლებელია, მომხმარებელს დაუბრუნდება შესაბამისი გადახდილი თანხა.`,
        ],
      },
      {
        heading: "5. რა შემთხვევაში არ ხდება თანხის დაბრუნება",
        paragraphs: [
          "პერსონალიზებული პროდუქტის სპეციფიკიდან გამომდინარე, თანხის დაბრუნება შეიძლება არ გავრცელდეს შემთხვევაზე, როდესაც:",
        ],
        bullets: [
          "პროდუქტი სწორად შეიქმნა მომხმარებლის მიერ მითითებული მონაცემების საფუძველზე;",
          "მომხმარებელმა თავად მიუთითა არასწორი სახელი, ასაკი ან სხვა ინფორმაცია;",
          "მომხმარებელმა ატვირთა დაბალი ხარისხის/არასწორი ფოტო და შედეგი სწორედ ამ მასალას უკავშირდება;",
          "მომხმარებელმა უბრალოდ გადაიფიქრა უკვე შექმნილი პერსონალიზებული პროდუქტის შეძენა;",
          `პრობლემა გამოწვეულია მომხმარებლის მოწყობილობით ან ინტერნეტთან წვდომით და არა ${BRAND_NAME}-ს სისტემით.`,
        ],
      },
      {
        heading: "6. როგორ მოითხოვოთ ანაზღაურება",
        paragraphs: [
          `მოგვწერეთ ${BRAND_NAME}-ს საკონტაქტო არხზე — ${MERCHANT.email} ან [კონტაქტის გვერდიდან](/contact) — და მიუთითეთ შეკვეთის ნომერი, პრობლემის მოკლე აღწერა და, საჭიროების შემთხვევაში, ფოტო/ვიდეო, რომელიც პრობლემის იდენტიფიცირებაში დაგვეხმარება.`,
          "მომართვის მიღებას დაგიდასტურებთ, ხოლო არაუგვიანეს 5 სამუშაო დღის განმავლობაში განვიხილავთ შემთხვევას და შესაბამისი საფუძვლის არსებობისას განვახორციელებთ:",
        ],
        checks: [
          "თანხის სრულ დაბრუნებას",
          "ნაწილობრივ ანაზღაურებას",
          "უფასო ხელახალ დამზადებას",
          "უფასო ჩანაცვლებას/ხელახალ მიწოდებას",
        ],
        afterBullets: [
          `ჩვენი პრინციპი მარტივია: თუ შეცდომა ${BRAND_NAME}-ს მხრიდანაა, მის გამოსწორებას მომხმარებელს დამატებითი ხარჯი არ უნდა მოჰყვეს.`,
        ],
      },
    ],
  },
  {
    id: "availability",
    title: "9. Service availability",
    paragraphs: [
      "We aim to keep the service available and reliable, but we do not guarantee uninterrupted access. Maintenance, third-party outages (including AI or cloud providers), or force majeure may cause delays or failures in story or PDF generation.",
    ],
  },
  {
    id: "liability",
    title: "10. Limitation of liability",
    paragraphs: [
      `To the fullest extent permitted by law, ${BRAND_NAME} and its operator are not liable for indirect, incidental, special, or consequential damages arising from your use of the service or reliance on AI-generated content.`,
      "Our total liability for any claim relating to the service is limited to the amount you paid us for the relevant book in the twelve (12) months before the claim, or zero if you paid nothing.",
      "Nothing in these terms limits liability that cannot be excluded under applicable law (including certain consumer rights in the EU/EEA).",
    ],
  },
  {
    id: "termination",
    title: "11. Suspension and termination",
    paragraphs: [
      "We may suspend or terminate access if you breach these terms, misuse the service, or if required for legal or security reasons.",
      "You may stop using the service at any time. To request account or data deletion, contact us (see Privacy Policy).",
    ],
  },
  {
    id: "changes",
    title: "12. Changes to these terms",
    paragraphs: [
      "We may update these Terms & Conditions from time to time. The “Last updated” date at the top will change when we do.",
      "Continued use of the service after changes take effect means you accept the updated terms. For material changes, we may provide additional notice where appropriate.",
    ],
  },
  {
    id: "contact",
    title: "13. Contact",
    paragraphs: [
      `Questions about these terms: [Contact us](/contact) or email ${LEGAL_CONTACT_EMAIL}.`,
    ],
  },
];
