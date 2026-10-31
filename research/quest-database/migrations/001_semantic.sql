PRAGMA foreign_keys=ON;
PRAGMA user_version=1;
CREATE TABLE metadata(key TEXT PRIMARY KEY, value TEXT NOT NULL) WITHOUT ROWID;
CREATE TABLE variants(variant_id TEXT PRIMARY KEY, game_variant TEXT NOT NULL, server TEXT, assurance TEXT NOT NULL) WITHOUT ROWID;
CREATE TABLE sources(
 source_id TEXT PRIMARY KEY, url TEXT NOT NULL, revision TEXT NOT NULL,
 kind TEXT NOT NULL, archive_path TEXT, license TEXT, original_text TEXT,
 UNIQUE(url,revision,kind)
) WITHOUT ROWID;
CREATE TABLE quests(
 quest_id TEXT PRIMARY KEY, original_name TEXT NOT NULL, category TEXT NOT NULL,
 variant_id TEXT NOT NULL REFERENCES variants, source_id TEXT NOT NULL REFERENCES sources,
 requirements_original TEXT, rewards_original TEXT, base_level INTEGER, job_level INTEGER,
 review_state TEXT NOT NULL, availability TEXT NOT NULL DEFAULT 'unknown',
 input_hash TEXT NOT NULL
) WITHOUT ROWID;
CREATE TABLE quest_steps(
 step_id TEXT PRIMARY KEY, quest_id TEXT NOT NULL REFERENCES quests ON DELETE CASCADE,
 step_order INTEGER NOT NULL CHECK(step_order>0), source_step INTEGER, source_kind TEXT,
 section TEXT, original_instruction TEXT NOT NULL, instruction_sha256 TEXT NOT NULL,
 primary_goal TEXT NOT NULL, review_state TEXT NOT NULL,
 UNIQUE(quest_id,step_order)
) WITHOUT ROWID;
CREATE TABLE evidence(
 evidence_id TEXT PRIMARY KEY, source_id TEXT NOT NULL REFERENCES sources,
 quest_id TEXT REFERENCES quests ON DELETE CASCADE, step_id TEXT REFERENCES quest_steps ON DELETE CASCADE,
 snippet TEXT NOT NULL CHECK(length(snippet)>0), method TEXT NOT NULL,
 status TEXT NOT NULL CHECK(status IN ('source_literal','context_inferred','candidate','reviewed','server_verified')),
 note TEXT, CHECK(status <> 'server_verified')
) WITHOUT ROWID;
CREATE TABLE evidence_context(
 evidence_id TEXT NOT NULL REFERENCES evidence ON DELETE CASCADE,
 supporting_id TEXT NOT NULL REFERENCES evidence ON DELETE CASCADE,
 PRIMARY KEY(evidence_id,supporting_id)
) WITHOUT ROWID;
CREATE TABLE entities(
 entity_id TEXT PRIMARY KEY, entity_type TEXT NOT NULL CHECK(entity_type IN ('npc','map','item','monster')),
 original_name TEXT NOT NULL, variant_id TEXT NOT NULL REFERENCES variants,
 evidence_id TEXT NOT NULL REFERENCES evidence, UNIQUE(entity_id,entity_type)
) WITHOUT ROWID;
CREATE TABLE npcs(
 entity_id TEXT PRIMARY KEY, entity_type TEXT NOT NULL DEFAULT 'npc' CHECK(entity_type='npc'),
 game_id INTEGER, identity_scope TEXT NOT NULL,
 FOREIGN KEY(entity_id,entity_type) REFERENCES entities(entity_id,entity_type)
) WITHOUT ROWID;
CREATE TABLE maps(
 entity_id TEXT PRIMARY KEY, entity_type TEXT NOT NULL DEFAULT 'map' CHECK(entity_type='map'),
 map_code TEXT, known_in_reference INTEGER NOT NULL CHECK(known_in_reference IN (0,1)),
 width INTEGER, height INTEGER,
 FOREIGN KEY(entity_id,entity_type) REFERENCES entities(entity_id,entity_type)
) WITHOUT ROWID;
CREATE TABLE items(
 entity_id TEXT PRIMARY KEY, entity_type TEXT NOT NULL DEFAULT 'item' CHECK(entity_type='item'),
 game_id INTEGER, icon_url TEXT, id_source TEXT,
 FOREIGN KEY(entity_id,entity_type) REFERENCES entities(entity_id,entity_type)
) WITHOUT ROWID;
CREATE TABLE monsters(
 entity_id TEXT PRIMARY KEY, entity_type TEXT NOT NULL DEFAULT 'monster' CHECK(entity_type='monster'),
 game_id INTEGER, id_source TEXT,
 FOREIGN KEY(entity_id,entity_type) REFERENCES entities(entity_id,entity_type)
) WITHOUT ROWID;
CREATE TABLE entity_aliases(
 entity_id TEXT NOT NULL REFERENCES entities, alias TEXT NOT NULL,
 evidence_id TEXT NOT NULL REFERENCES evidence, PRIMARY KEY(entity_id,alias)
) WITHOUT ROWID;
CREATE TABLE entity_evidence(
 entity_id TEXT NOT NULL REFERENCES entities, evidence_id TEXT NOT NULL REFERENCES evidence,
 PRIMARY KEY(entity_id,evidence_id)
) WITHOUT ROWID;
CREATE TABLE locations(
 location_id TEXT PRIMARY KEY, map_id TEXT REFERENCES maps(entity_id),
 x INTEGER NOT NULL CHECK(x BETWEEN 0 AND 400), y INTEGER NOT NULL CHECK(y BETWEEN 0 AND 400),
 evidence_id TEXT NOT NULL REFERENCES evidence,
 point_verified INTEGER NOT NULL DEFAULT 0 CHECK(point_verified=0)
) WITHOUT ROWID;
CREATE TABLE step_locations(
 step_id TEXT NOT NULL REFERENCES quest_steps ON DELETE CASCADE,
 location_id TEXT NOT NULL REFERENCES locations, role TEXT NOT NULL,
 evidence_id TEXT NOT NULL REFERENCES evidence,
 PRIMARY KEY(step_id,location_id,role)
) WITHOUT ROWID;
CREATE TABLE step_npcs(
 step_id TEXT NOT NULL REFERENCES quest_steps ON DELETE CASCADE, npc_id TEXT NOT NULL REFERENCES npcs(entity_id),
 role TEXT NOT NULL, evidence_id TEXT NOT NULL REFERENCES evidence, PRIMARY KEY(step_id,npc_id,role)
) WITHOUT ROWID;
CREATE TABLE step_items(
 fact_id TEXT PRIMARY KEY, step_id TEXT NOT NULL REFERENCES quest_steps ON DELETE CASCADE,
 item_id TEXT NOT NULL REFERENCES items(entity_id), operation TEXT NOT NULL CHECK(operation IN ('collect','bring','deliver','buy','use','consume','possess','show','decline','mention')),
 quantity INTEGER CHECK(quantity>0), required INTEGER CHECK(required IN (0,1)),
 condition_text TEXT, evidence_id TEXT NOT NULL REFERENCES evidence
) WITHOUT ROWID;
CREATE TABLE step_monsters(
 fact_id TEXT PRIMARY KEY, step_id TEXT NOT NULL REFERENCES quest_steps ON DELETE CASCADE,
 monster_id TEXT NOT NULL REFERENCES monsters(entity_id), operation TEXT NOT NULL CHECK(operation IN ('kill','loot_source','mention')),
 quantity INTEGER CHECK(quantity>0), required INTEGER CHECK(required IN (0,1)),
 condition_text TEXT, evidence_id TEXT NOT NULL REFERENCES evidence
) WITHOUT ROWID;
CREATE TABLE requirements(
 requirement_id TEXT PRIMARY KEY, quest_id TEXT NOT NULL REFERENCES quests ON DELETE CASCADE,
 step_id TEXT REFERENCES quest_steps ON DELETE CASCADE, kind TEXT NOT NULL,
 entity_id TEXT REFERENCES entities, amount TEXT, condition_text TEXT,
 evidence_id TEXT NOT NULL REFERENCES evidence
) WITHOUT ROWID;
CREATE TABLE rewards(
 reward_id TEXT PRIMARY KEY, quest_id TEXT NOT NULL REFERENCES quests ON DELETE CASCADE,
 kind TEXT NOT NULL, item_id TEXT REFERENCES items(entity_id), amount TEXT,
 condition_text TEXT, evidence_id TEXT NOT NULL REFERENCES evidence
) WITHOUT ROWID;
CREATE TABLE step_rewards(
 step_id TEXT NOT NULL REFERENCES quest_steps ON DELETE CASCADE, reward_id TEXT NOT NULL REFERENCES rewards,
 PRIMARY KEY(step_id,reward_id)
) WITHOUT ROWID;
CREATE TABLE dialogues(
 dialogue_id TEXT PRIMARY KEY, npc_id TEXT REFERENCES npcs(entity_id),
 original_text TEXT, evidence_id TEXT NOT NULL REFERENCES evidence
) WITHOUT ROWID;
CREATE TABLE dialogue_options(
 option_id TEXT PRIMARY KEY, dialogue_id TEXT NOT NULL REFERENCES dialogues,
 option_order INTEGER NOT NULL CHECK(option_order>0), exact_text TEXT NOT NULL,
 correct INTEGER CHECK(correct IN (0,1)), operation TEXT NOT NULL,
 condition_text TEXT, evidence_id TEXT NOT NULL REFERENCES evidence,
 UNIQUE(dialogue_id,option_order)
) WITHOUT ROWID;
CREATE TABLE step_dialogues(
 step_id TEXT NOT NULL REFERENCES quest_steps ON DELETE CASCADE, dialogue_id TEXT NOT NULL REFERENCES dialogues,
 dialogue_order INTEGER NOT NULL, PRIMARY KEY(step_id,dialogue_id)
) WITHOUT ROWID;
CREATE TABLE step_actions(
 action_id TEXT PRIMARY KEY, step_id TEXT NOT NULL REFERENCES quest_steps ON DELETE CASCADE,
 action_order INTEGER NOT NULL, operation TEXT NOT NULL, entity_id TEXT REFERENCES entities,
 condition_text TEXT, evidence_id TEXT NOT NULL REFERENCES evidence,
 UNIQUE(step_id,action_order)
) WITHOUT ROWID;
CREATE TABLE step_images(
 image_id TEXT PRIMARY KEY, step_id TEXT NOT NULL REFERENCES quest_steps ON DELETE CASCADE,
 source_url TEXT NOT NULL, local_path TEXT, purpose TEXT NOT NULL, caption TEXT,
 rights_status TEXT NOT NULL, evidence_id TEXT NOT NULL REFERENCES evidence
) WITHOUT ROWID;
CREATE TABLE step_descriptions(
 step_id TEXT PRIMARY KEY REFERENCES quest_steps ON DELETE CASCADE,
 short_ru TEXT, short_en TEXT, full_ru TEXT, full_en TEXT,
 generation_version TEXT NOT NULL, review_state TEXT NOT NULL,
 source_instruction_sha256 TEXT NOT NULL, editor TEXT,
 CHECK(short_ru IS NULL OR length(trim(short_ru))>0),
 CHECK(short_en IS NULL OR length(trim(short_en))>0),
 CHECK(full_ru IS NULL OR length(trim(full_ru))>0),
 CHECK(full_en IS NULL OR length(trim(full_en))>0)
) WITHOUT ROWID;
CREATE TABLE description_mentions(
 mention_id TEXT PRIMARY KEY, step_id TEXT NOT NULL REFERENCES step_descriptions ON DELETE CASCADE,
 field TEXT NOT NULL CHECK(field IN ('short_ru','short_en','full_ru','full_en')),
 start_offset INTEGER NOT NULL CHECK(start_offset>=0), end_offset INTEGER NOT NULL,
 entity_id TEXT NOT NULL, entity_type TEXT NOT NULL, surface TEXT NOT NULL,
 evidence_id TEXT NOT NULL REFERENCES evidence,
 CHECK(end_offset>start_offset),
 FOREIGN KEY(entity_id,entity_type) REFERENCES entities(entity_id,entity_type),
 UNIQUE(step_id,field,start_offset,end_offset)
) WITHOUT ROWID;
CREATE TABLE step_edges(
 from_step TEXT NOT NULL REFERENCES quest_steps, to_step TEXT NOT NULL REFERENCES quest_steps,
 kind TEXT NOT NULL, condition_text TEXT, evidence_id TEXT NOT NULL REFERENCES evidence,
 PRIMARY KEY(from_step,to_step,kind)
) WITHOUT ROWID;
CREATE TABLE quest_relations(
 quest_id TEXT NOT NULL REFERENCES quests, related_id TEXT NOT NULL REFERENCES quests,
 kind TEXT NOT NULL, evidence_id TEXT NOT NULL REFERENCES evidence,
 PRIMARY KEY(quest_id,related_id,kind)
) WITHOUT ROWID;
CREATE TABLE review_queue(
 review_id TEXT PRIMARY KEY, quest_id TEXT NOT NULL REFERENCES quests ON DELETE CASCADE,
 step_id TEXT REFERENCES quest_steps ON DELETE CASCADE, reason TEXT NOT NULL,
 detail TEXT NOT NULL, evidence_id TEXT NOT NULL REFERENCES evidence,
 severity TEXT NOT NULL CHECK(severity IN ('info','warning','blocking'))
) WITHOUT ROWID;
CREATE INDEX review_by_quest ON review_queue(quest_id,step_id);
CREATE INDEX mentions_by_entity ON description_mentions(entity_id,step_id);
CREATE INDEX steps_by_quest ON quest_steps(quest_id,step_order);
CREATE VIEW localized_steps AS SELECT s.*,d.short_ru,d.short_en,d.full_ru,d.full_en
 FROM quest_steps s JOIN step_descriptions d USING(step_id);
CREATE VIEW ready_steps AS SELECT * FROM localized_steps WHERE
 short_ru IS NOT NULL AND short_en IS NOT NULL AND full_ru IS NOT NULL AND full_en IS NOT NULL;
