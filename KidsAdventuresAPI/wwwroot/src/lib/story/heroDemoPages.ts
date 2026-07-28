import type { StoryPageContent } from "@/lib/api/types";
import { WORLD_COVER_ART, type WorldId } from "@/lib/worlds";

const PLACE: Record<WorldId, string> = {
  dinosaurs: "დაკარგული ხეობა",
  space: "ვარსკვლავების გზა",
  pirates: "მბრწყინავი კუნძული",
  animals: "მოჯადოებული ტყე",
  airplanes: "ღრუბლების გზა",
  magic: "სინათლის ქალაქი",
};

/**
 * Demo sample pages used by the Partner Demo hero storybook (`ea()` in app.js).
 * Keeps the landing page page-turn / scroll experience without a paid book.
 */
export function heroDemoPages(
  heroName: string,
  theme: WorldId = "dinosaurs",
): StoryPageContent[] {
  const place = PLACE[theme] ?? PLACE.dinosaurs;
  const cover = WORLD_COVER_ART[theme] ?? WORLD_COVER_ART.dinosaurs;
  const friendLine = "იქ პატარა რექსი ელოდებოდა.";

  return [
    {
      title: "გვერდი 1 · ოქროსფერი რუკა",
      caption: "გვერდი 1 · ოქროსფერი რუკა",
      content: `ერთ დილას ${heroName}-მ ძველ წიგნში ოქროსფერი გზა შენიშნა. გზა ${place}-ისკენ მიდიოდა და სწორედ მისი სახელით იწყებოდა.`,
      illustrationUrl: cover,
      isIllustrated: true,
    },
    {
      title: "გვერდი 2 · ნაცნობი მეგობარი",
      caption: "გვერდი 2 · ნაცნობი მეგობარი",
      content: `${friendLine} მან ${heroName}-ს ხელი გაუწოდა და უთხრა, რომ ამ გზას მხოლოდ ნამდვილი მეგობრები გაივლიდნენ.`,
      illustrationUrl: cover,
      isIllustrated: true,
    },
    {
      title: "გვერდი 3 · საიდუმლო კარიბჭე",
      caption: "გვერდი 3 · საიდუმლო კარიბჭე",
      content: `ერთად მათ ${place}-ის დამალული კარიბჭე გააღეს. კარის მიღმა მანათობელი ნიშნები ახალ გამოცდამდე მიუძღოდათ.`,
      illustrationUrl: cover,
      isIllustrated: true,
    },
    {
      title: "გვერდი 4 · მეგობრობის ნიშანი",
      caption: "გვერდი 4 · მეგობრობის ნიშანი",
      content:
        "ნიშანი მხოლოდ მაშინ ანათებდა, როცა მეგობრები ერთმანეთს უსმენდნენ. მათ პასუხი ერთად იპოვეს და გზა კვლავ განათდა.",
      illustrationUrl: cover,
      isIllustrated: true,
    },
    {
      title: "გვერდი 5 · ყველაზე მამაცი ნაბიჯი",
      caption: "გვერდი 5 · ყველაზე მამაცი ნაბიჯი",
      content: `${heroName}-ს ცოტა შეეშინდა, მაგრამ რექსმა წინა გამარჯვებები შეახსენა. მათ ყველაზე რთული მომენტი პატარა, მამაცი ნაბიჯებით გაიარეს.`,
      illustrationUrl: cover,
      isIllustrated: true,
    },
    {
      title: "გვერდი 6 · ახალი მოგონება",
      caption: "გვერდი 6 · ახალი მოგონება",
      content:
        "საღამოს ცაზე კიდევ ერთი ვარსკვლავი აინთო. სამყაროს რუკამ მათი ახალი მოგონება ოქროსფერი ხაზით შეინახა.",
      illustrationUrl: cover,
      isIllustrated: true,
    },
    {
      title: "გვერდი 7 · გზა გრძელდება",
      caption: "გვერდი 7 · გზა გრძელდება",
      content: `ბოლო გვერდზე გზა არ დასრულებულა. ის ${heroName}-ს შემდეგი თავგადასავლებისკენ მიუთითებდა — იქ, სადაც ძველი მეგობრები კვლავ შეხვდებიან.`,
      illustrationUrl: cover,
      isIllustrated: true,
    },
  ];
}
