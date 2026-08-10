import type { StoryPageContent } from "@/lib/api/types";
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

function spreads(heroName: string, placeIn: string): DemoSpread[] {
  return [
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
}

/**
 * The book on the landing page, laid out the way a printed one is.
 *
 * Two things were wrong with it. Every page carried the same picture — the world's cover, seven
 * times — so the sample showed a parent a book whose pages never change. And each page drew the
 * artwork and the words together, which is the old single-page format; the books we actually
 * print are spreads, a full-bleed illustration facing a page of prose. The demo is the strongest
 * claim the landing page makes about the product, so it should be the product.
 *
 * The shape here is the one MasterStoryProjection builds for a real book: each spread becomes two
 * pages, the picture first with an empty `content`, then the facing prose with no illustration,
 * both carrying the same title and caption.
 *
 * Art only exists for the dinosaur valley, which is the world the hero section shows. Any other
 * world falls back to its cover rather than putting a boy and a sauropod in the wrong place.
 */
export function heroDemoPages(heroName: string, theme: WorldId = "dinosaurs"): StoryPageContent[] {
  const placeIn = PLACE_IN[theme] ?? PLACE_IN.dinosaurs;
  const art = (image: number) =>
    theme === "dinosaurs"
      ? `/adventrya/hero-demo/page-${image}.webp`
      : (WORLD_COVER_ART[theme] ?? WORLD_COVER_ART.dinosaurs);

  return spreads(heroName, placeIn).flatMap((spread) => [
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
