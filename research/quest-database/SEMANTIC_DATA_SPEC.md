# ROQuestie — structured bilingual quest database specification

Status: **user-approved product/data requirements, 2026-10-31**.
Phase: **database research, semantic enrichment, source verification and dataset export only**.
Application integration, RoAura WPF/overlays, packaging and release are **explicitly DEFERRED** until a separate user request. Do not change application code or create an integration workstream under this phase.
This document overrides older research-stage prose about retaining old event quests in the active database. Historical stage descriptions are not current requirements.

## Objective

Produce a queryable, normalized, reusable knowledge base of permanent Ragnarok Online quests and their stages. Do not require future UI logic to rediscover NPCs, item names, coordinates or dialogue answers from free-form prose. Separate typed entities from localized narrative content and preserve the complete original source text for audit and retracing.

### Working database and quest exclusions

- Exclude **all archived, retired and historical event-only quests** from the active working quest database, semantic enrichment workload, and all generated *usable* catalogs/exports. Removing them from the visible UI alone is insufficient.
- Do not classify an active permanent, repeatable or currently valid quest as archived merely because the title contains `Event`, a date, or a seasonal term; validate the status against source evidence and flag ambiguity for review.
- Unrelated NPC/event *source code* used as provenance is not an active quest; do not conflate source archives with the working quest catalog.
- Preserve referential integrity: delete/exclude dependent steps, associations, orphaned entities where appropriate, and recompute coverage metrics and tests.
- No event removal is claimed complete by this document; this is a requirement for the upcoming dataset transformation.

## Required normalized model

Use stable immutable IDs, typed records and many-to-many relations where relevant; exact final physical SQL schema may be designed and tested during the database phase.

- `Quests`: canonical quest key, original display name, category, known server/source variant, prerequisites, overall rewards and review evidence.
- `QuestSteps`: stable step identity and order, original source text, **short_ru**, **short_en**, **full_ru**, **full_en**, source anchors and semantic review status.
- `NPCs`: original in-game names, canonical/source IDs where verified, source variant and provenance. One NPC record may be linked by several stages or quests.
- `Maps`, `Locations`: canonical in-game map code/name, X/Y, role (talk-to NPC, destination, entrance, return point), map-known status and confidence.
- `Items`: original in-game item name, verified Item ID and image reference if available.
- `Monsters`: original in-game name and verified ID, for kill/loot objectives.
- `Dialogues`, `DialogueOptions`: participants, source dialogue text, optional localized explanatory text, exact answers/choices, order/branch conditions and evidence.
- `Rewards`: item/EXP/Zeny/other outcome, amount and conditions; distinguish rewards from requirements.
- `StepImages`: optional one-to-many links to sourced/authorized screenshots or illustrations; source, image type, caption and licensing/availability status. Missing images stay missing, never hallucinated.
- Relations such as `StepNPCs`, `StepLocations`, `StepItems`, `StepMonsters`, `StepDialogues`, `StepRewards` with typed role, required count and source references. Quests may relate to the same canonical entity without duplicated labels.

For highlighted text, use **typed entity references and/or verified source span offsets/segments** linked to entity IDs in both localized descriptions. Rendering policy (e.g. every NPC name blue) must apply by type to those references; do not implement this by keyword search of step text. Description-generation must preserve link alignment when possible. Never rely on visual color as the stored semantic type.

## Bilingual descriptions and immutable in-game names — mandatory

- Every included step requires **Russian AND English short and full descriptions**, each independently stored and independently validated: `short_ru`, `short_en`, `full_ru`, `full_en`.
- Preserve `original_instruction` exactly in its source language, even after creating edited/clarified full walkthroughs.
- **DO NOT translate, transliterate, locally rename, or adapt the proper names of cities, maps, NPCs, items or monsters** when producing Russian or English descriptions. Keep the source's actual in-game spellings and case. Use map codes such as `prontera` literally when provided. Also preserve exact game dialogue answer text that must be selected or typed.
- Natural-language instructions may be translated: e.g. `Speak to Old Man in Prontera and bring 10 Jellopy` / `Поговорите с Old Man в Prontera и принесите 10 Jellopy`. Both `Old Man`, `Prontera`, `Jellopy` remain unchanged. Source game labels can be separately aliased only with explicit evidence and must not silently replace the canonical display string.
- Only meaningful prose (verbs, sentence structure, hints) changes by locale. Numbers, item counts, map codes, coordinates, branching order and expected responses must stay consistent in RU and EN.
- A missing or ambiguous proper noun is marked unverified/unknown rather than guessed; retain raw wording, source hash and uncertainty metadata.

## Contextual quest analysis and evidence

Analyze the **whole quest**, prerequisites, rewards, step before/after, and sentence-level action context, not isolated token or regex matches. Extract concrete tasks: talk, move, collect, deliver, buy, consume, use, kill, select answer, return, receive reward. Distinguish required materials from awards, optional items and mere mentions. Link ambiguous pronouns (e.g. `return to him`) to a prior NPC only if unambiguous and source supported.

Coordinates: parse `prontera 249,132`, `prontera 249 132`, `prontera (249,132)`, `prontera (249 132)` and other well-defined formats; infer neither map nor NPC from two plain numbers without evidence. A match in a reference wiki or emulator script is a **candidate** and is not proof of server-specific location. Avoid accepting generic counts/levels as coordinates.

For each extracted assertion store at minimum: quest/step, entity link, role, literal supporting evidence/snippet, source URL and hash where available, variant, confidence/verification status, conflicts and manual-review requirement. Do not present inferred information as verified gameplay facts.

Generated short description: concise actionable objective suitable for later overlays, preserving prerequisites, item quantities, choices and order. Full description: exact actionable context, directions and branches. Original source text is never overwritten. AI-generated semantic records must be reviewed against original text and subjected to contradiction/coverage checks before promotion.

## Work phases — current authorization

1. **Filter** historical/archived event-only quests from working data and output; audit exclusions.
2. **Design and migrate** normalized SQLite model and relationship/index contracts; retain source/provenance information.
3. **Semantically enrich** all retained quests and stages in full context.
4. **Produce RU+EN** concise and full descriptions with original in-game names unmodified, and attach structured images only when available.
5. **Validate** semantics, cross-language parity, IDs, navigation candidates, source variants, orphan references and migration/quality regressions; generate review queues and versioned database export.

**Out of scope until separately authorized:** integrate new database into RoAura, update WPF UI, overlays, styles, settings, release builds, app deployment and application runtime migrations. Prior integration documents/specs may be read later but do not imply current authorization.

## Acceptance criteria

- All active quest records have ordered steps; every retained step has four required localized descriptions and preserved original source content.
- Stage-linked structured NPC/map/coordinates/items/loot/rewards/dialogue/options/images are independently addressable; optional missing data explicitly represented.
- Canonical proper names are unchanged in both languages, including inside shortened descriptions and answer choices.
- No archived event-only quests appear in working catalogs or shipped quest-data exports; ambiguous archival status is explicitly reviewed.
- A future consumer can style/identify all NPC mentions through typed relationships/spans with **no string matching** against prose.
- Source provenance, source/game-server variants and uncertainty are retained; no false claims of verified server compatibility.
- This phase creates **no RoAura application integration, package or release**.
