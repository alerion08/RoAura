-- Source-backed additions enrich descriptions without rewriting archived instructions.
CREATE TABLE step_source_additions(
 addition_id TEXT PRIMARY KEY,
 step_id TEXT NOT NULL REFERENCES quest_steps ON DELETE CASCADE,
 addition_order INTEGER NOT NULL CHECK(addition_order>0),
 text_ru TEXT NOT NULL CHECK(length(trim(text_ru))>0),
 text_en TEXT NOT NULL CHECK(length(trim(text_en))>0),
 evidence_id TEXT NOT NULL REFERENCES evidence,
 review_state TEXT NOT NULL DEFAULT 'source_reviewed',
 UNIQUE(step_id,addition_order)
) WITHOUT ROWID;
INSERT OR REPLACE INTO metadata(key,value) VALUES('schema_version','3');
PRAGMA user_version=3;
