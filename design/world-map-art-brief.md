# Adventrya — /themes რუკის და ბეკის სავიზუალო ბრიფი

ეს დოკუმენტი ორ რამეს აკეთებს: აფიქსირებს **კომპოზიციას**, რომელზეც კოდი აეწყობა, და
გაძლევს **მზა პრომპტებს**, რომ სურათები დახატო.

---

## 0. ყველაზე მნიშვნელოვანი წესი

იმ მოკაპებზე, რაც გამომიგზავნე, კუნძულებს **თავზე აწერია** სახელები და აზის ფერადი
ემბლემები. ეს ყველაფერი **აპლიკაციამ უნდა დახატოს, არა მხატვარმა.**

სურათში არ უნდა იყოს:

- არანაირი ტექსტი, არც ქართული, არც ლათინური
- არანაირი წრიული ფერადი ბეიჯი / იკონი კუნძულებზე
- არანაირი ღილაკი, ისარი, ბარათი, ჩარჩო
- არანაირი ინტერფეისი

რატომ: წარწერა ენაზეა დამოკიდებული (გვაქვს ქართული და ინგლისური), ბეიჯი კი მდგომარეობაზე
— აირჩია თუ არა, ჩაკეტილია თუ არა. სურათში ჩახატული ისინი გაიყინება. ხატულის ადგილას
დატოვე **მანათობელი კარიბჭე / პორტალი** — მასზე დაჯდება ბეიჯი.

---

## 1. კომპოზიციის ბადე

ექვსივე სამყარო ერთ სცენაშია: გადაშლილი წიგნიდან ამოზრდილი მცურავი კუნძულები, შუაში
ოქროსფერი ბილიკი, ბილიკის ბოლოში — ბავშვი და ბეკი ზურგით.

კუნძულები **მარცხენა და მარჯვენა კიდეებზეა, სამ-სამი.** შუა ვერტიკალური დერეფანი და
კუნძულებს შორის ცა **მუქი და ცარიელი უნდა დარჩეს** — იქ დაჯდება წარწერები.

### ჰორიზონტალური (დესკტოპი), 3:2

პროცენტები სურათის სიგანე/სიმაღლიდან. წერტილი = კუნძულის **მანათობელი კარიბჭის ცენტრი**.

| სამყარო | კარიბჭე | რა არის |
|---|---|---|
| კოსმოსი | 26% / 22% | ობსერვატორია და პორტალი ვარსკვლავთა ნისლეულში |
| თვითმფრინავები | 75% / 20% | ღრუბლების ზემოთ ამოსული ციხე-ქალაქი, დირიჟაბლი |
| დინოზავრები | 20% / 50% | ხეობა ჩანჩქერით, ბრახიოზავრი |
| ცხოველები | 79% / 52% | მოჩუქურთმებული ხის კარიბჭე, ფარნები, მელა და დათვი |
| მეკობრეები | 23% / 76% | ლაგუნა, ხომალდი, პალმები, წყალქვეშა ნათება |
| ჯადოსნური | 77% / 74% | ფარნებით სავსე ქალაქი, გუმბათები |
| ბავშვი + ბეკი | 49% / 82% | ზურგით, გადაშლილ წიგნზე, ბილიკის დასაწყისში |

### ვერტიკალური (მობაილი), 9:16

**ცალკე სურათია, არა ჰორიზონტალურის ამოჭრა.** იგივე ექვსი კუნძული, უფრო აწყობილი
ვერტიკალურად.

| სამყარო | კარიბჭე |
|---|---|
| კოსმოსი | 32% / 26% |
| თვითმფრინავები | 74% / 22% |
| დინოზავრები | 22% / 44% |
| ცხოველები | 76% / 47% |
| მეკობრეები | 24% / 63% |
| ჯადოსნური | 75% / 65% |
| ბავშვი + ბეკი | 47% / 79% |

ორივეში: ზედა 12% და ქვედა 10% შედარებით მშვიდი — ზემოთ ჰედერი აზის, ქვემოთ ღილაკი.

---

## 2. პრომპტი — ჰორიზონტალური რუკა (3:2)

```
A single wide storybook scene, painted digital illustration, warm and dreamlike,
Pixar-adjacent lighting with hand-painted fantasy-book texture.

An enormous open leather-bound book lies on a dark wooden table at the very bottom
of the frame. Out of its pages a whole world grows upward: six floating islands
suspended in a deep indigo and violet night sky full of stars and soft nebula clouds.
A single glowing golden path of light winds up the centre of the image from the open
pages toward the top, linking the islands.

Island placement, precise:
- upper left, centred near 26% across and 22% down: a cliff observatory with a domed
  telescope and a standing ring-shaped portal glowing pale violet, set against a star
  nebula
- upper right, near 75% across and 20% down: a castle town on top of towering
  moonlit clouds, small airship drifting nearby, glowing archway at its base
- middle left, near 20% across and 50% down: a green valley island with a waterfall,
  tall ferns and a long-necked dinosaur, a lit stone gateway at the cliff edge
- middle right, near 79% across and 52% down: an ancient carved tree with a glowing
  doorway in its trunk, hanging lanterns, a fox and a bear in the grass
- lower left, near 23% across and 76% down: a turquoise lagoon island with palm trees
  and a wooden sailing ship, glowing underwater light, a lit stone arch on the shore
- lower right, near 77% across and 74% down: a warm amber city of domes and hanging
  lanterns, a glowing gate at its entrance

At the bottom centre, near 49% across and 82% down, seen from behind and small in the
frame: a child in a explorer coat and backpack, holding the hand of a tiny glowing
companion creature. They stand on the open pages at the start of the golden path.

Critical: the vertical centre corridor and the sky between the islands must stay dark,
empty and uncluttered. Each island is clearly separated from the others by empty night
sky. No island touches another.

Absolutely no text, no letters, no numbers, no logos, no user interface, no buttons,
no arrows, no coloured circular badges or icons on the islands. Pure painted scene only.
```

**პარამეტრები:** 3:2, ყველაზე მაღალი ხარისხი, 4K თუ შესაძლებელია.

---

## 3. პრომპტი — ვერტიკალური რუკა (9:16)

იგივე ტექსტი, მხოლოდ კუნძულების კოორდინატები და ბოლო აბზაცი შეიცვალა:

```
A single tall storybook scene, painted digital illustration, warm and dreamlike,
Pixar-adjacent lighting with hand-painted fantasy-book texture.

An enormous open leather-bound book lies at the very bottom of the frame. Out of its
pages a whole world grows upward: six floating islands suspended in a deep indigo and
violet night sky full of stars and soft nebula clouds. A single glowing golden path of
light winds up the centre of the image from the open pages toward the top.

Island placement, precise:
- near 32% across and 26% down: a cliff observatory with a domed telescope and a
  standing ring-shaped portal glowing pale violet, against a star nebula
- near 74% across and 22% down: a castle town on towering moonlit clouds, small airship,
  glowing archway at its base
- near 22% across and 44% down: a green valley island with a waterfall, tall ferns and a
  long-necked dinosaur, a lit stone gateway at the cliff edge
- near 76% across and 47% down: an ancient carved tree with a glowing doorway in its
  trunk, hanging lanterns, a fox and a bear
- near 24% across and 63% down: a turquoise lagoon island with palm trees and a wooden
  sailing ship, glowing underwater light, a lit stone arch on the shore
- near 75% across and 65% down: a warm amber city of domes and hanging lanterns, a
  glowing gate at its entrance

At the bottom centre, near 47% across and 79% down, seen from behind and small: a child
in an explorer coat and backpack holding the hand of a tiny glowing companion creature,
standing on the open pages at the start of the golden path.

Critical: the vertical centre corridor and the sky between the islands must stay dark,
empty and uncluttered. Each island clearly separated by empty night sky.

Absolutely no text, no letters, no numbers, no logos, no user interface, no buttons,
no arrows, no coloured circular badges or icons on the islands. Pure painted scene only.
```

**პარამეტრები:** 9:16, ყველაზე მაღალი ხარისხი.

---

## 4. პრომპტი — ბეკი (ახალი, სპრაიტი)

გვჭირდება **გამჭვირვალე ფონზე**, ცალკეული პოზები. თუ ინსტრუმენტი გამჭვირვალობას არ
აკეთებს — **ერთფეროვანი ღია ფონი** (ისეთი, როგორიც რეფერენსზეა), ფონს მე მოვაცილებ.

```
Character sheet of a single small magical creature, nine separate poses, evenly spaced
on a plain flat cream background, no shadows cast on the background.

The creature: a tiny round-bodied sprite the size of a toddler, deep violet-purple skin,
very large warm amber eyes with soft highlights, a gentle friendly smile, small four-
fingered hands. Its whole body is wrapped in flowing pale parchment and dried-leaf robes
that curl upward from its head into a single tall curling wisp, like a page caught in
wind. A small warm golden light glows at its chest, like a lantern under the parchment.
Soft painted 3D look, warm rim light, children's picture-book character design.

The nine poses: floating with both hands open in welcome; waving hello with one hand
raised; pointing forward with an eager expression; gesturing to one side as if showing
something; hovering calmly with hands together; tumbling playfully mid-air; both arms
raised in celebration; cupping the glowing light in both hands, looking down at it;
looking back over its shoulder and beckoning to follow.

Consistent character design across all nine. No text, no numbers, no labels, no frames.
```

**პარამეტრები:** 2:3, მაღალი ხარისხი.

### რა პოზები მჭირდება კოდისთვის

გვერდზე ბეკი რეაგირებს იმაზე, რასაც მშობელი აკეთებს. მინიმუმ ეს ოთხი:

| პოზა | როდის ჩანს |
|---|---|
| მისალმება (ხელი აწეული) | გვერდი გაიხსნა, ჯერ არაფერია არჩეული |
| მიმანიშნებელი (ხელით უჩვენებს) | თაგვი კუნძულზეა, ჯერ არ დაუწკაპუნებია |
| აღფრთოვანება (ხელები აწეული) | სამყარო აირჩია |
| მოწოდება (უკან იხედება) | მზადაა გასაგრძელებლად |

თუ ცხრავე პოზა გამოვა, დანარჩენს მოგვიანებით გამოვიყენებთ ზღაპრის შექმნის ეკრანზე.

---

## 5. რაც უნდა იცოდე ბეკის შეცვლაზე

ბეკი უკვე ჩაშენებულია წიგნის გენერაციაში, არა მარტო საიტზე:

- `KidsAdventuresAPI/Assets/Beki/beki-canonical-v1.png` — ეს ფაილი ერთვის ყოველი იმ
  გვერდის გენერაციას, სადაც ბეკი მონაწილეობს
- `BekiOptions.BekiDesignMatch = 0.90` — ყოველი დახატული ილუსტრაცია ქულდება **ზუსტად ამ
  ფაილთან მსგავსებაზე**; 0.90-ზე ქვემოთ გვერდი ბრაკდება
- PDF-ის ბოლო ყდაზე ბეკის პორტრეტი დგას

ანუ ახალი ბეკის ატვირთვა = `beki-canonical-v2`, და შემდეგ ზღვრების გადამოწმება რეალურ
გენერაციაზე. **ჯერ საიტზე დავაყენოთ, წიგნის პაიპლაინს ცალკე შევეხოთ** — თორემ ერთ
ცვლილებაში ორი განსხვავებული რისკი აირევა.
