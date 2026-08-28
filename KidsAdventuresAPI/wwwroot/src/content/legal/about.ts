import type { LegalSection } from "@/components/legal/LegalDocument";
import { BRAND_NAME } from "@/lib/brand";
import { MERCHANT } from "@/lib/merchant";

/*
  Who we are, in Georgian.

  It is filed with the legal content rather than written as marketing because it is doing a
  legal job: an acquirer, and a parent about to type a card number, both want to know that a
  real company stands behind the site. Everything here is a plain description of what the
  product does — nothing about the team or the roadmap that would need maintaining.
*/

export const aboutIntro =
  `${BRAND_NAME} ქმნის პერსონალურ საბავშვო წიგნებს, სადაც მთავარი გმირი თქვენი ბავშვია — ` +
  `მისი სახელით, გარეგნობით და თქვენ მიერ არჩეული სამყაროთი.`;

export const aboutSections: LegalSection[] = [
  {
    id: "what",
    title: "რას ვაკეთებთ",
    paragraphs: [
      "ყოველი წიგნი თავიდან იწერება. თქვენ ირჩევთ ბავშვს, სამყაროს და თემას, ატვირთავთ ერთ ფოტოს — და რამდენიმე წუთში იღებთ დასურათებულ ამბავს, რომელშიც გმირი თქვენი ბავშვია.",
      "ტექსტს და ილუსტრაციებს ქმნის ხელოვნური ინტელექტი, ჩვენი გუნდის მიერ დაწერილი წესებით: ამბავი ბავშვის ასაკს შეესაბამება, ილუსტრაციებზე სახე ერთი და იგივე რჩება ყველა გვერდზე, ხოლო შინაარსი ბავშვისთვის უსაფრთხოა.",
      "წიგნი ხელმისაწვდომია ორ ფორმატში: ციფრულად — წასაკითხად და ჩამოსატვირთად, და ნაბეჭდად — მაგარყდიანი წიგნი, რომელიც მისამართზე მოგაქვთ.",
    ],
  },
  {
    id: "language",
    title: "ენა და ბაზარი",
    paragraphs: [
      "წიგნები იქმნება ქართულ და ინგლისურ ენებზე. ბეჭდვას და მიწოდებას ამჟამად საქართველოს მასშტაბით ვახორციელებთ.",
    ],
  },
  {
    id: "photos",
    title: "ბავშვის ფოტო",
    paragraphs: [
      "ატვირთული ფოტო გამოიყენება მხოლოდ ერთი მიზნით — რომ ილუსტრაციაზე გმირი თქვენს ბავშვს დაემსგავსოს. ფოტოებს არ ვყიდით, არ ვუზიარებთ რეკლამისთვის და არ ვიყენებთ სხვა მომხმარებლის წიგნში.",
      "დეტალურად ეს [კონფიდენციალურობის პოლიტიკაშია](/privacy) აღწერილი.",
    ],
  },
  {
    id: "company",
    title: "კომპანია",
    paragraphs: [
      MERCHANT.legalName
        ? `ვებგვერდს ოპერირებს ${MERCHANT.legalName}, რეგისტრირებული საქართველოში (ს/კ ${MERCHANT.taxId}).`
        : `ვებგვერდი ოპერირებს საქართველოში, საიდენტიფიკაციო კოდით ${MERCHANT.taxId}.`,
      "სრული საკონტაქტო ინფორმაცია — მისამართი, ტელეფონი და ელფოსტა — [კონტაქტის გვერდზეა](/contact).",
    ],
  },
  {
    id: "policies",
    title: "წესები",
    paragraphs: [
      "[მიწოდება, გაუქმება და თანხის დაბრუნება](/refunds) — როგორ მიდის შეკვეთა და რა ხდება, თუ რამე არ გამოვიდა.",
      "[წესები და პირობები](/terms) — ვებგვერდით სარგებლობის პირობები.",
      "[კონფიდენციალურობის პოლიტიკა](/privacy) — რა მონაცემებს ვამუშავებთ და რატომ.",
    ],
  },
];
