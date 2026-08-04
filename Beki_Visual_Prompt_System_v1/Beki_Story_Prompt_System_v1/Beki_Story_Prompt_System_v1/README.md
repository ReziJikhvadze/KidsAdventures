# Beki Story Prompt System v1

ეს პაკეტი შეიცავს Beki-ს 12-გვერდიანი პერსონალიზებული ზღაპრის production pipeline-ის პირველ სრულ ვერსიას.

## ფაილები

```text
prompts/
  story-generator-v1.md   — პირველი draft-ის შექმნა
  story-reviewer-v1.md    — ქართული, სიუჟეტური, ასაკობრივი და continuity review
  story-repair-v1.md      — მხოლოდ validator-ის ტექნიკური შეცდომების გასწორება

schemas/
  story-input-v1.schema.json
  story-output-v1.schema.json

examples/
  tini-production-input.json

docs/
  backend-integration.md
  qa-test-plan.md
```

## გამოყენების თანმიმდევრობა

```text
1. Validate input
2. Story Generator
3. Validate draft
4. Story Reviewer
5. Validate final story
6. Repair once, only if needed
7. Save final story and continuation memory
```

## მნიშვნელოვანი საზღვარი

ამ პაკეტში **არ არის Visual Prompt Generator**. მოცემული საწყისი prompt მხოლოდ სთორის გენერაციას ეხებოდა. ვიზუალური prompt-ის ცალკე ვერსია ჯერ უნდა მოგვაწოდოთ ანალიზისთვის; ამის შემდეგ ის დაემატება როგორც დამოუკიდებელი pipeline დამტკიცებული სთორის შემდეგ.

## ძირითადი product decisions, რომლებიც v1-ში დაფიქსირდა

- ყდა ცალკეა; შემდეგ მოდის ზუსტად 12 interior page;
- reader-facing ტექსტი არის გამართული ქართული;
- ასაკობრივი ჯგუფებია 2–4, 5–7 და 8–10;
- ბავშვი არის მთავარი გმირი და იღებს გადამწყვეტ გადაწყვეტილებებს;
- Beki არის recurring guide/friend და მნიშვნელოვნად ჩნდება 3–5 გვერდზე;
- Extra Wish მონაწილეობს მინიმუმ სამ narrative beat-ში;
- მიმდინარე პრობლემა სრულდება, დიდი თავგადასავალი ღია რჩება;
- Page 12-ზე CTA ცალკე field-შია QR layout-ისთვის;
- Memory Engine ინახავს ურთიერთობებს, ობიექტებს, დაპირებებს, open threads-ს და recent plot patterns-ს;
- random seed backend-ში ირჩევა და არა prompt-ის შიგნით;
- Generator, Reviewer და Validator ცალკე პასუხისმგებლობებია.

## Versioning

ფაილების სახელები და prompt-ის შიდა version ორივე შეინახეთ DB-ში. ცვლილებისას ძველი prompt არ გადააწეროთ; შექმენით `v1.1`, `v1.2`, შემდეგ `v2.0`.
