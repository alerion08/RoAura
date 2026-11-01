-- Alternatives spanning several archived steps and independent optional activities.
CREATE TABLE quest_routes(
 route_id TEXT PRIMARY KEY,
 quest_id TEXT NOT NULL REFERENCES quests ON DELETE CASCADE,
 route_key TEXT NOT NULL,
 kind TEXT NOT NULL CHECK(kind IN ('alternative','optional')),
 choice_group TEXT,
 condition_text TEXT NOT NULL CHECK(length(trim(condition_text))>0),
 evidence_id TEXT NOT NULL REFERENCES evidence,
 review_state TEXT NOT NULL DEFAULT 'source_reviewed_not_server_verified',
 UNIQUE(quest_id,route_key),
 CHECK((kind='alternative' AND choice_group IS NOT NULL AND length(trim(choice_group))>0)
    OR (kind='optional' AND choice_group IS NULL))
) WITHOUT ROWID;
CREATE TABLE step_routes(
 step_id TEXT NOT NULL REFERENCES quest_steps ON DELETE CASCADE,
 route_id TEXT NOT NULL REFERENCES quest_routes ON DELETE CASCADE,
 route_order INTEGER NOT NULL CHECK(route_order>0),
 evidence_id TEXT NOT NULL REFERENCES evidence,
 PRIMARY KEY(step_id,route_id), UNIQUE(route_id,route_order)
) WITHOUT ROWID;
CREATE INDEX route_steps ON step_routes(route_id,route_order);
ALTER TABLE step_locations ADD COLUMN location_order INTEGER CHECK(location_order>0);
ALTER TABLE step_locations ADD COLUMN condition_text TEXT;
CREATE UNIQUE INDEX ordered_step_locations ON step_locations(step_id,location_order) WHERE location_order IS NOT NULL;
INSERT OR REPLACE INTO metadata(key,value) VALUES('schema_version','4');
PRAGMA user_version=4;
