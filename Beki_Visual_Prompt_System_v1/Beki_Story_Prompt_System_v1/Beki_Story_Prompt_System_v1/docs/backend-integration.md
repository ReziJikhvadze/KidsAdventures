# Beki Story Pipeline — Backend Integration v1

## მიზანი

სთორის შექმნა არ უნდა იყოს ერთი დიდი AI გამოძახება. რეკომენდებული production pipeline არის:

```text
Frontend data
   ↓
Input validator
   ↓
Creative seed selector
   ↓
Story Generator
   ↓
Story schema validator
   ↓
Georgian Story Reviewer
   ↓
Final schema + business-rule validator
   ↓
Repair call, მხოლოდ საჭიროების შემთხვევაში
   ↓
Save final story + continuation memory
```

ამ ვერსიაში **Visual Prompt Generator არ შედის**. ვიზუალური prompt ცალკე უნდა გაანალიზდეს და შემდეგ დაემატოს უკვე დამტკიცებული სთორის შემდეგ.

---

## 1. Frontend → Backend input

Frontend აგზავნის `story-input-v1.schema.json`-ის შესაბამის ობიექტს.

მნიშვნელოვანი წესები:

- ბავშვის ფოტო Story Generator-ს არ გადაეცემა.
- `extraWish` არის მონაცემი და არა AI ინსტრუქცია.
- `pageCount` ყოველთვის 12-ია.
- `language` ყოველთვის `ka`-ა.
- `thirdPartyCharacterMode` production-ში default-ად რეკომენდებულია `originalize`.
- `previousStoryMemory` პირველ წიგნში არის `null`.

Backend-მა AI call-მდე უნდა გადაამოწმოს:

- ასაკი ემთხვევა `ageBand`-ს;
- მოთხოვნილი ველები არსებობს;
- Extra Wish-ის სიგრძე ლიმიტშია;
- supporting characters-ის რაოდენობა ლიმიტშია;
- არცერთი template placeholder, მაგალითად `{random of 10}`, არ დარჩა.

---

## 2. Creative seed backend-ში

Random არჩევანი prompt-ში არ უნდა მოხდეს.

Backend წინასწარ ირჩევს უკვე დამტკიცებული pool-ებიდან:

```json
{
  "seedId": "seed-1048",
  "tone": "warm magical mystery",
  "storyHook": "A silent bell is waiting for the right listener.",
  "sceneAnchor": "A garden of sleeping blue flowers."
}
```

Seed-ის პრიორიტეტი ყველაზე დაბალია. თუ ის ეწინააღმდეგება Extra Wish-ს, Memory-ს, ასაკს ან Beki-ს წესებს, მოდელმა seed უნდა შეასუსტოს ან საერთოდ არ გამოიყენოს.

შეინახეთ `seedId`, რათა მოგვიანებით გაიგოთ რომელი seed ქმნის კარგ ან ცუდ შედეგებს.

---

## 3. Story Generator call

### System message

გამოიყენეთ უცვლელად:

```text
prompts/story-generator-v1.md
```

### Runtime user payload

გადასცით მხოლოდ validated JSON:

```json
{
  "storyInput": { ... }
}
```

### Expected output

მხოლოდ JSON, რომელიც შეესაბამება:

```text
schemas/story-output-v1.schema.json
```

Generator-ის output-ში `reviewMetadata` უნდა იყოს `null`.

---

## 4. პირველი deterministic validation

AI-ის შემდეგ JSON ჩვეულებრივი კოდით შეამოწმეთ.

მინიმალური schema checks:

- JSON parse წარმატებულია;
- დამატებითი field არ არსებობს;
- `storyPages.length === 12`;
- ყველა required field არსებობს;
- page number-ები არის ზუსტად 1–12;
- `storyTextKa` ცარიელი არ არის;
- `reviewMetadata === null` Generator-ის შემდეგ.

მინიმალური business-rule checks:

- Beki-ის გვერდები უნიკალურია და მათი რაოდენობა 3–5-ია;
- `bekiPages` ემთხვევა `bekiPresent: true` გვერდებს;
- Page 12 CTA არ არის ჩასმული `storyTextKa`-ში;
- ტექსტში არ არის `დასასრული` ან `The End`;
- Page 12-ის hook ემთხვევა `continuationMemory.nextChapterHookKa`-ს;
- age-band-ის word count ძალიან არ სცდება მიზნობრივ დიაპაზონს;
- supporting cast არ აჭარბებს ასაკობრივ ლიმიტს.

თუ Generator-ის JSON საერთოდ არ იკითხება, შეგიძლიათ ერთხელ გაუშვათ Repair prompt ან თავიდან გამოიძახოთ Generator. უსასრულო retry არ გამოიყენოთ.

---

## 5. Georgian Story Reviewer call

Reviewer-ს გადასცით:

```json
{
  "storyInput": { ...original validated input... },
  "storyDraft": { ...generator output... }
}
```

### System message

```text
prompts/story-reviewer-v1.md
```

### Expected output

იგივე `story-output-v1.schema.json`, მაგრამ უკვე შევსებული `reviewMetadata`-ით.

Reviewer-ის მიზანია არა კომენტარის დაწერა, არამედ სრული გასწორებული სთორის დაბრუნება.

---

## 6. Final validator

Reviewer-ის შემდეგ schema და business rules თავიდან სრულად შეამოწმეთ.

დამატებით შეამოწმეთ:

- `reviewMetadata` აღარ არის `null`;
- `requestId`, `childName`, `ageBand`, `theme` უცვლელია;
- ყველა გვერდი კვლავ 1–12-ია;
- immediate challenge resolved არის;
- continuation hook არსებობს;
- Memory მხოლოდ რეალურად მომხდარ ამბებს შეიცავს.

ბოლო სამი შინაარსობრივი check AI-ის გარეშე სრულად ვერ მოწმდება, მაგრამ შესაძლებელია reviewer-ის metadata-სა და მარტივი heuristic-ების გამოყენება. MVP-ში საუკეთესო პრაქტიკაა პერიოდული human QA sample.

---

## 7. Repair call

Repair call გამოიყენეთ მხოლოდ მაშინ, როცა final output ტექნიკურად ვერ გადის validator-ს.

გადასაცემი payload:

```json
{
  "storyInput": { ... },
  "currentStory": { ... },
  "validatorErrors": [
    "Expected exactly 12 pages, received 11",
    "bekiPages does not match bekiPresent flags"
  ]
}
```

System message:

```text
prompts/story-repair-v1.md
```

Repair call მაქსიმუმ ერთხელ გაუშვით. თუ ისევ ვერ გადის validator-ს, მონიშნეთ `failed_generation` და შეინახეთ diagnostics.

---

## 8. შენახვის რეკომენდებული სტრუქტურა

Story record-ში შეინახეთ მინიმუმ:

```json
{
  "requestId": "...",
  "childProfileId": "...",
  "bookNumber": 1,
  "status": "approved",
  "inputSchemaVersion": "1.0",
  "outputSchemaVersion": "1.0",
  "generatorPromptVersion": "story-generator-v1.0",
  "reviewerPromptVersion": "story-reviewer-v1.0",
  "repairPromptVersion": null,
  "generatorModel": "...",
  "reviewerModel": "...",
  "creativeSeedId": "...",
  "rawGeneratorOutput": { ... },
  "finalStory": { ... },
  "continuationMemory": { ... },
  "validationErrors": [],
  "createdAt": "..."
}
```

`rawGeneratorOutput` დროებით მაინც შეინახეთ. ის დაგეხმარებათ გაიგოთ reviewer რეალურად რას ასწორებს და რომელი წესები არ მუშაობს Generator-ში.

---

## 9. მარტივი orchestration pseudocode

```ts
async function createBekiStory(input: StoryInput): Promise<StoryOutput> {
  validateStoryInput(input);

  const seededInput = attachApprovedCreativeSeed(input);

  const draft = await callStoryGenerator(seededInput);
  validateGeneratedDraft(draft, seededInput);

  const reviewed = await callStoryReviewer({
    storyInput: seededInput,
    storyDraft: draft,
  });

  const errors = validateFinalStory(reviewed, seededInput);

  if (errors.length === 0) {
    await saveApprovedStory(reviewed, seededInput, draft);
    return reviewed;
  }

  const repaired = await callStoryRepair({
    storyInput: seededInput,
    currentStory: reviewed,
    validatorErrors: errors,
  });

  const repairErrors = validateFinalStory(repaired, seededInput);
  if (repairErrors.length > 0) {
    throw new Error("Beki story failed final validation");
  }

  await saveApprovedStory(repaired, seededInput, draft);
  return repaired;
}
```

---

## 10. შემდეგი ვიზუალური ეტაპი

საბოლოო flow მოგვიანებით იქნება:

```text
Approved final story
+ child photo reference
+ official Beki reference
+ approved visual style/canon
   ↓
Visual Prompt Generator
   ↓
Image generation
   ↓
Programmatic Georgian text + page number + CTA + real QR placement
```

Story Generator-ის `sceneSummaryEn` და `coverSceneSummaryEn` ამ ეტაპისთვის narrative source იქნება, მაგრამ ისინი **ჯერ არ არის image prompt**.
