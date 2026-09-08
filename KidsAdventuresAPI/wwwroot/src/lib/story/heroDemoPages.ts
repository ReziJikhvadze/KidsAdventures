import type { StoryPageContent } from "@/lib/api/types";
import type { PlateSpread } from "@/components/adventrya/storybook/StorybookVolume";
import { WORLD_COVER_ART, type WorldId } from "@/lib/worlds";

/**
 * "Into that place", written out rather than built by gluing ში onto a nominative name.
 *
 * Georgian will not have it either way: the adjective agrees with the noun and loses its ending
 * ("დაკარგული ხეობა" but "დაკარგულ ხეობაში"), and a place takes ში or ზე depending on the word.
 * Six entries are cheaper than a rule that would be wrong for half of them.
 */
const PLACE_IN: Record<WorldId, string> = {
  dinosaurs: "დაკარგულ ხეობაში",
  space: "ვარსკვლავების გზაზე",
  pirates: "მბრწყინავ კუნძულზე",
  animals: "მოჯადოებულ ტყეში",
  airplanes: "ღრუბლების გზაზე",
  magic: "სინათლის ქალაქში",
};

/**
 * Georgian declines a name, it does not hyphenate one. The demo read "ზუკა-ს ბაბუას" and
 * "ზუკა-მ", which is the shape a template leaves behind rather than anything a Georgian speaker
 * writes — on the one page meant to show a parent what their child's book will read like.
 *
 * A name ending in a vowel takes -მ in the ergative and -ს in the genitive; one ending in a
 * consonant takes -მა and -ის. The dative is -ს either way.
 */
const VOWELS = "აეიოუ";
const endsInVowel = (name: string) => VOWELS.includes(name.trim().slice(-1));
/** Did something: ზუკამ / ნიკოლოზმა */
const erg = (name: string) => `${name.trim()}${endsInVowel(name) ? "მ" : "მა"}`;
/** Had or was given something: ზუკას / ნიკოლოზს */
const dat = (name: string) => `${name.trim()}ს`;
/** Belonged to: ზუკას / ნიკოლოზის */
const gen = (name: string) => `${name.trim()}${endsInVowel(name) ? "ს" : "ის"}`;

/**
 * One spread: a picture and the page of words facing it.
 *
 * `image` names the file rather than being derived from the spread's position, so a beat can be
 * added or reordered without renaming artwork — which is how a picture ends up on the wrong page.
 */
type DemoSpread = { title: string; caption: string; text: string; image: number };

/**
 * The printed book, word for word.
 *
 * Lifted out of "ზუკა და სინათლის ქალაქი" — a real Beki book, print PDF dated 2026-09-08 — so the
 * sample on the home page is not a sample at all. The words are the ones on the press sheets and
 * the pictures are the same nine illustrations, pulled out of that file at 5315 x 2480 and resized
 * for the web without recomposing anything.
 *
 * `caption` is empty on purpose. The printed story spreads carry no rubric over the prose — the
 * text sits alone in its panel — and a heading invented for the screen would be the one part of
 * this book that never went to press.
 */
const CITY_OF_LIGHT = (heroName: string): DemoSpread[] => [
  {
    title: "ჩამქრალი ფარანი",
    caption: "",
    text: `${heroName} სინათლის ქალაქში დგას. მთელი ქალაქი თბილად ანათებს. მხოლოდ ${dat(heroName)} პატარა ფარანია ჩამქრალი. – ნეტავ, როგორ ავანთოთ? – ფიქრობს ${heroName}. ბეკი გვერდით უდგას და უღიმის.`,
    image: 1,
  },
  {
    title: "ოქროსფერი მტვერი",
    caption: "",
    text: `უცებ ჰაერში ოქროსფერი მტვერი ფარფატებს. ის ნელა მიედინება მთავარი ქუჩისკენ. – შეხედე, ბეკი! – იძახის ${heroName}. – ეს ხომ სინათლის ბილიკია! მეგობრები ბილიკს მიჰყვებიან.`,
    image: 2,
  },
  {
    title: "მანათობელი ხიდი",
    caption: "",
    text: `ბილიკი მანათობელ ხიდზე გადადის. ${heroName} თამამად მიაბიჯებს შუშის საფეხურებზე. ჩამქრალი ფარანი მაგრად უჭირავს ხელში. ბეკიც მხიარულად მიჰყვება უკან.`,
    image: 3,
  },
  {
    title: "ბილიკი წყდება",
    caption: "",
    text: `მოულოდნელად, ხიდის ბოლოს ბილიკი წყდება. ირგვლივ სიბნელეა. ${dat(heroName)} ფარანი ოდნავ ციმციმებს და ისევ ქრება. – სად წავიდეთ? – ჩურჩულებს ${heroName}. ბეკი ყურს უგდებს სიჩუმეს.`,
    image: 4,
  },
  {
    title: "ლამპრები ინთება",
    caption: "",
    text: `${heroName} ამჩნევს, რომ სიბნელეში რაღაც წკრიალებს. ის ნელა იწყებს მელოდიის ღიღინს. ქუჩის ლამპრები სათითაოდ ინთება! ისინი პირდაპირ დიდი კოშკისკენ მიუთითებენ. ბეკი სიხარულით ტაშს უკრავს.`,
    image: 5,
  },
  {
    title: "სინათლის გული",
    caption: "",
    text: `კოშკის წინ უზარმაზარი შადრევანია. იქ წყლის მაგივრად თბილი შუქი ჩქეფს. ეს ხომ სინათლის გულია! ${heroName} გაოცებული იყურება ზემოთ. ბეკი ღიმილით უყურებს მეგობარს.`,
    image: 6,
  },
  {
    title: "ფარანი ინთება",
    caption: "",
    text: `${heroName} ფარანს შადრევანში ყოფს. შუშა ოქროსფრად ივსება და მზესავით ანათებს. ყველაფერი გამოვიდა! ${heroName} ბედნიერია, ბეკი კი მხიარულად ხტუნავს. ახლა მათი გზა სულ ნათელია.`,
    image: 7,
  },
  {
    title: "ლურჯი ვარსკვლავი",
    caption: "",
    text: `მთელი ქალაქი ჯადოსნურად ციმციმებს. ${heroName} თავის მანათობელ ფარანს მაღლა სწევს. უცებ, ცაში პატარა ლურჯი ვარსკვლავი ეშვება. ნეტავ იქ რა ხდება? მეგობრები ერთმანეთს უღიმიან.`,
    image: 8,
  },
];

/**
 * What the printed book opens on before the story: the two plates between cover and page one.
 *
 * Both lifted from the same PDF as the spreads. The endpaper is the approved 450 x 210mm
 * pattern (`BEKI_Endpaper_Pattern_Approved_450x210mm_300ppi_sRGB.png`), pasted down on the
 * left board with the free leaf on the right left bare, which is how page 2 of the file
 * renders. The dedication is page 3: Beki alone at the gate, and in the cream panel the four
 * lines the press sets at 24.3, 14.4, 18 and 18pt, ranged left, centred on the leaf.
 *
 * The age is the printed book's own — this is a sample of one real copy, not a template.
 */
export function heroDemoFrontMatter(heroName: string): PlateSpread[] {
  const name = heroName.trim();
  return [
    { art: "/adventrya/hero-demo/endpaper.webp", plainRight: true },
    {
      art: "/adventrya/hero-demo/title.webp",
      panelSide: "left",
      centred: true,
      panel: [
        { text: `ეს წიგნი ეკუთვნის ${dat(name)}`, size: "lg" },
        { text: "4 წლის", size: "sm" },
        { text: `„${name} და სინათლის ქალაქი“` },
        { text: `${name}, ერთად გავუყვებით ამ ბილიკს. დროა, დაიწყოს ჩვენი თავგადასავალი!` },
      ],
    },
  ];
}

/**
 * The older dinosaur demo, kept for the worlds the printed book does not cover.
 *
 * Its artwork is `hero-demo/page-*.webp` — 900 x 1350 portraits drawn one to a page, which is the
 * pre-spread format. They are not spreads and must not be stretched across a fold; the component
 * measures every illustration and falls back to the page-at-a-time layout for them by itself.
 */
const LOST_VALLEY = (heroName: string, placeIn: string): DemoSpread[] => [
  {
    title: "ძველი რუკა",
    caption: "ძველი წიგნი და ოქროსფერი გზა",
    text: `${gen(heroName)} ბაბუას ძველი წიგნი ჰქონდა. ერთ დილას ${erg(heroName)} ის გადაშალა. შიგნით რუკა იდო და რუკაზე ოქროსფერი გზა იყო დახატული.`,
    image: 1,
  },
  {
    title: "ტირილი გვიმრებში",
    caption: "ვიღაც ტიროდა ახლოს",
    text: `გზა ${placeIn} შედიოდა. ${heroName} გაჰყვა. უცებ გაჩერდა — სადღაც ახლოს ვიღაც ტიროდა.`,
    image: 2,
  },
  {
    title: "ქვა ქვაზე",
    caption: "ხელები დაეღალა, მაგრამ არ გაჩერდა",
    text: `ქვების ქვეშ პატარა დინოზავრს ფეხი გაება. ${erg(heroName)} ქვები სათითაოდ გადაწია. ხელები დაეღალა, მაგრამ არ გაჩერდა.`,
    image: 3,
  },
  {
    title: "ერთად გზაზე",
    caption: "ორნი ერთ გზაზე",
    text: `პატარა რექსი გამოვიდა და ფეხზე წამოდგა. მერე ${dat(heroName)} გვერდით ამოუდგა. ოქროსფერ გზას ერთად გაჰყვნენ.`,
    image: 8,
  },
  {
    title: "გატეხილი ხიდი",
    caption: "მდინარე და გატეხილი ხიდი",
    text: "მალე მდინარესთან მივიდნენ. ხიდი გატეხილი იყო. რექსმა უკან დაიხია.",
    image: 4,
  },
  {
    title: "პირველი ნაბიჯი",
    caption: "პირველი ნაბიჯი ფიცარზე",
    text: `${dat(heroName)}აც ეშინოდა. მაგრამ ხელი გაუწოდა და პირველმა დადგა ფიცარზე. ნელა, ერთად გაიარეს.`,
    image: 5,
  },
  {
    title: "მეორე ნაპირზე",
    caption: "დედა მეორე ნაპირზე ელოდა",
    text: `მეორე ნაპირზე რექსის დედა ელოდა. მან გრძელი ყელი დახარა და ${dat(heroName)} ნაზად შეეხო. რექსი დედის ფეხს მიეკრა.`,
    image: 6,
  },
  {
    title: "რუკა ისევ წიგნში",
    caption: "გზა შინისკენ ანათებდა",
    text: `შინ რომ ბრუნდებოდა, გზა ოქროსფრად ანათებდა. ${erg(heroName)} რუკა ისევ წიგნში ჩადო და თაროზე დადო.`,
    image: 7,
  },
];

/**
 * The book on the landing page, laid out the way a printed one is.
 *
 * The shape here is the one MasterStoryProjection builds for a real book: each spread becomes two
 * pages, the picture first with an empty `content`, then the facing prose with no illustration,
 * both carrying the same title and caption.
 *
 * Two sets of artwork, and they are not interchangeable. The magic world is served by the real
 * book's own spreads — 2.143:1, one painting across an open sheet — which is what makes the home
 * page show the product rather than a drawing of it. Every other world still has the older
 * portraits, one per page. Anything with no art at all falls back to its world cover rather than
 * putting a boy and a sauropod in the wrong place.
 */
export function heroDemoPages(heroName: string, theme: WorldId = "magic"): StoryPageContent[] {
  const placeIn = PLACE_IN[theme] ?? PLACE_IN.magic;

  const art = (image: number) => {
    if (theme === "magic") return `/adventrya/hero-demo/spread-${image}.webp`;
    if (theme === "dinosaurs") return `/adventrya/hero-demo/page-${image}.webp`;
    return WORLD_COVER_ART[theme] ?? WORLD_COVER_ART.dinosaurs;
  };

  const beats =
    theme === "magic" ? CITY_OF_LIGHT(heroName.trim()) : LOST_VALLEY(heroName.trim(), placeIn);

  return beats.flatMap((spread) => [
    {
      title: spread.title,
      caption: spread.caption,
      content: "",
      illustrationUrl: art(spread.image),
      isIllustrated: true,
      isTextOnlyPage: false,
    },
    {
      title: spread.title,
      caption: spread.caption,
      content: spread.text,
      isTextOnlyPage: true,
    },
  ]);
}
