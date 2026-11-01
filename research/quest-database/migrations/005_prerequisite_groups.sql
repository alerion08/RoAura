-- Source-reviewed threshold groups; generic prerequisite mentions stay separate.
CREATE TABLE quest_prerequisite_groups(
 group_id TEXT PRIMARY KEY,
 quest_id TEXT NOT NULL REFERENCES quests ON DELETE CASCADE,
 group_key TEXT NOT NULL,
 minimum_completed INTEGER NOT NULL CHECK(minimum_completed>0),
 evidence_id TEXT NOT NULL REFERENCES evidence,
 review_state TEXT NOT NULL DEFAULT 'source_reviewed_not_server_verified',
 UNIQUE(quest_id,group_key)
) WITHOUT ROWID;
CREATE TABLE quest_prerequisite_members(
 group_id TEXT NOT NULL REFERENCES quest_prerequisite_groups ON DELETE CASCADE,
 related_id TEXT NOT NULL REFERENCES quests,
 member_order INTEGER NOT NULL CHECK(member_order>0),
 evidence_id TEXT NOT NULL REFERENCES evidence,
 PRIMARY KEY(group_id,related_id), UNIQUE(group_id,member_order)
) WITHOUT ROWID;
INSERT OR REPLACE INTO metadata(key,value) VALUES('schema_version','5');
PRAGMA user_version=5;
