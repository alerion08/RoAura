-- Additive migration: existing quest/step/entity IDs and human descriptions survive.
CREATE TABLE step_branches(
 branch_id TEXT PRIMARY KEY, step_id TEXT NOT NULL REFERENCES quest_steps ON DELETE CASCADE,
 branch_key TEXT NOT NULL, choice_group TEXT NOT NULL,
 variant_scope TEXT NOT NULL, condition_text TEXT NOT NULL,
 evidence_id TEXT NOT NULL REFERENCES evidence,
 review_state TEXT NOT NULL DEFAULT 'source_reviewed_not_server_verified',
 UNIQUE(step_id,branch_key), UNIQUE(branch_id,step_id)
) WITHOUT ROWID;
CREATE TABLE branch_descriptions(
 branch_id TEXT PRIMARY KEY REFERENCES step_branches ON DELETE CASCADE,
 short_ru TEXT NOT NULL CHECK(length(trim(short_ru))>0),
 short_en TEXT NOT NULL CHECK(length(trim(short_en))>0),
 full_ru TEXT NOT NULL CHECK(length(trim(full_ru))>0),
 full_en TEXT NOT NULL CHECK(length(trim(full_en))>0),
 generation_version TEXT NOT NULL, review_state TEXT NOT NULL,
 source_instruction_sha256 TEXT NOT NULL, editor TEXT
) WITHOUT ROWID;
CREATE TABLE branch_mentions(
 mention_id TEXT PRIMARY KEY, branch_id TEXT NOT NULL REFERENCES branch_descriptions ON DELETE CASCADE,
 field TEXT NOT NULL CHECK(field IN ('short_ru','short_en','full_ru','full_en')),
 start_offset INTEGER NOT NULL CHECK(start_offset>=0), end_offset INTEGER NOT NULL,
 entity_id TEXT NOT NULL, entity_type TEXT NOT NULL, surface TEXT NOT NULL,
 evidence_id TEXT NOT NULL REFERENCES evidence,
 CHECK(end_offset>start_offset),
 FOREIGN KEY(entity_id,entity_type) REFERENCES entities(entity_id,entity_type),
 UNIQUE(branch_id,field,start_offset,end_offset)
) WITHOUT ROWID;
CREATE TABLE source_fragments(
 fragment_id TEXT PRIMARY KEY,
 origin_step_id TEXT NOT NULL REFERENCES quest_steps,
 target_step_id TEXT NOT NULL REFERENCES quest_steps,
 branch_id TEXT REFERENCES step_branches,
 partition_key TEXT,
 section TEXT NOT NULL, role TEXT NOT NULL CHECK(role IN ('primary','branch')),
 start_offset INTEGER NOT NULL CHECK(start_offset>=0), end_offset INTEGER NOT NULL,
 original_text TEXT NOT NULL, evidence_id TEXT NOT NULL REFERENCES evidence,
 CHECK(end_offset>start_offset),
 CHECK((role='primary' AND branch_id IS NULL) OR (role='branch' AND branch_id IS NOT NULL))
) WITHOUT ROWID;
ALTER TABLE step_actions ADD COLUMN branch_id TEXT REFERENCES step_branches;
ALTER TABLE step_items ADD COLUMN branch_id TEXT REFERENCES step_branches;
ALTER TABLE step_monsters ADD COLUMN branch_id TEXT REFERENCES step_branches;
ALTER TABLE requirements ADD COLUMN branch_id TEXT REFERENCES step_branches;
ALTER TABLE rewards ADD COLUMN branch_id TEXT REFERENCES step_branches;
ALTER TABLE dialogues ADD COLUMN branch_id TEXT REFERENCES step_branches;
CREATE INDEX branches_by_step ON step_branches(step_id,choice_group);
CREATE INDEX fragments_by_origin ON source_fragments(origin_step_id,start_offset);
INSERT OR REPLACE INTO metadata(key,value) VALUES('schema_version','2');
PRAGMA user_version=2;
