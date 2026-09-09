# Character consistency across spreads

The consistency change targets the supporting cast invented by each story: stars, dinosaurs,
animals and other recurring characters. The scenario planner registers them with stable names
and fixed descriptions of their anatomy, colors and markings, including personified objects.
Beki's existing native generation remains the default (`Beki:InsertBekiInGeneration=true`).
The optional exact-PNG compositor is still available when that flag is explicitly false.
No additional model review was enabled, and no preview stage was added.

The child keeps the original identity reference plus one fixed rendered appearance anchor.
Each recurring character or object now keeps its first planned appearance as its design source.
Subsequent pages never overwrite that source. A scene receives every relevant source, grouped
by source page, with explicit instructions identifying which characters each image controls.
A character's first appearance must finish before dependent pages start; established characters
can still be drawn in parallel. There is no extra image-generation call to create these sources.

This cache belongs to one book and is reconstructed from stored base images on resume. Missing
source artwork or source evidence causes its dependent pages to redraw together. New prompt
versions prevent partially generated books from combining the old and new generation contracts.
Completed books are not automatically regenerated or changed.

Prompt caching is a latency/cost optimization, not an identity guarantee:
https://developers.openai.com/api/docs/guides/prompt-caching
OpenAI still documents possible recurring-character drift:
https://developers.openai.com/api/docs/guides/image-generation
The generated child, Beki and story creatures remain reference-guided,
so new visual samples still require inspection.

Deployment requires no new environment setting. Beki's generation setting is unchanged.
The GPT Image 2.5 Flare model and the testing-flow switch are unchanged. Full-book generation
can wait longer when a new recurring character is introduced; the preview has no added work.

`LiveCompositeCharacterTests` is an opt-in two-image visual proof using repository test artwork.
Set `ADVENTRYA_CHARACTER_PROOF_KEY` through an authorized secret environment and optionally
`ADVENTRYA_CHARACTER_PROOF_OUTPUT`, then run only that test. It writes two spreads and their
prompts/receipts for visual comparison and makes paid OpenAI requests. Normal test runs skip it.
