# ADR-006: Local Persistence

## Status
Proposed

## Context
TypeRight must persist application configuration, vocabulary, personal dictionary, learned preferences, and replacement rules across sessions. The previous implementation had issues with configuration migration, data loss during upgrades, and unbounded storage growth.

## Decision
Use local JSON files for application configuration and a SQLite-based structured store for vocabulary and learned preferences, with versioned schemas and migration logic.

## Considered Options

### Option A: Single XML configuration file
- **Pros**: .NET has built-in XML support; XPath for queries; readable; human-editable
- **Cons**: Verbose; schema evolution difficult; XML complexity; vulnerable to malicious parsing; no built-in migration; performance degrades with size
- **Evidence**: Previous TypeRight used XML; migration between versions was brittle; users reported configuration loss on upgrade

### Option B: JSON configuration + custom binary vocabulary
- **Pros**: Human-readable; compact; .NET has System.Text.Json; easy to version; can embed schema version
- **Cons**: Custom migration logic needed for vocabulary; no relational query power; import/export must be hand-coded
- **Evidence**: JSON is the modern .NET standard; good for configuration but needs structured store for vocabulary

### Option C: SQLite for everything (SELECTED)
- **Pros**: Full SQL query power; transactional; built-in migration support (via schema version column); compact; concurrent read access; .NET has Microsoft.Data.Sqlite; easy import/export via `.dump` and `.csv`
- **Cons**: Additional dependency; requires SQL schema management; overkill for simple configuration; slight startup overhead
- **Evidence**: SQLite is embedded, serverless, zero-configuration; widely used in .NET desktop applications; Microsoft.Data.Sqlite is well-supported

### Option D: Cloud/Roaming storage
- **Pros**: Sync across devices; user expects it
- **Cons**: Privacy violation (violates ADR-007); requires internet connectivity; not available offline; compliance overhead; not appropriate for first release
- **Evidence**: TypeRight is offline-first; cloud sync can be added in a future release

## Selected Design
**Two-Store Persistence Architecture**

### 1. Application Configuration: JSON File
- **Location**: `%APPDATA%\TypeRight\config.json`
- **Schema version**: Embedded at top of file (`$schema` or custom `version` field)
- **Migration**: Schema version detected; migration functions run sequentially from installed version to current version
- **Contents**:
  - `version` (integer, major.minor format)
  - `correction_enabled` (boolean)
  - `correction_pause` (enum: Idle/Paused5m/Paused30m/PausedIndefinite/Resuming)
  - `startup_on_signin` (boolean)
  - `minimized_on_startup` (boolean)
  - `excluded_applications` (string array)
  - `english_variant` (enum: "auto", "en-US", "en-GB")
  - `learning_enabled` (boolean)
  - `suggestion_timeout_ms` (integer)
  - `auto_correct` (boolean)
  - `global_shortcut` (string - hotkey description)

- **Migration functions**:
  - v1→v2: add missing fields with defaults
  - v2→v3: reorganize structure, rename fields
  - etc.

- **Safe read**: If file corrupted or unreadable, use defaults and rewrite file
- **Autosave**: On every setting change; on shutdown

### 2. Vocabulary and Preferences: SQLite Database
- **Location**: `%LOCALAPPDATA%\TypeRight\vocabulary.db`
- **Schema version**: `meta` table with `schema_version` column
- **Tables**:
  - `base_vocabulary`: root words, spelling variants
  - `personal_dictionary`: user-added words
  - `learned_preferences`: accept/reject feedback
  - `replacement_rules`: custom rules
  - `ignored_suggestions`: words user marked as ignored
  - `meta`: schema version, migration tracking

- **Schema versioning**:
  ```sql
  CREATE TABLE meta (key TEXT PRIMARY KEY, value TEXT);
  INSERT INTO meta VALUES ('schema_version', '6');
  ```
- **Migration**: Add columns, rename tables, data transformation scripts
- **Backup**: On first run, copy original to `.bak`; migrations run on opening connection

### 3. Import/Export
- **Vocabulary export**: CSV from SQLite; user can edit and re-import
- **Personal dictionary export/import**: JSON subset of personal_dictionary table
- **Settings export/import**: JSON config file backup
- **No external dependencies**: All files remain on local machine

### 4. Concurrency
- **Configuration**: Single-threaded access; FileStream.Lock during read-modify-write
- **SQLite**: WAL mode for concurrent reads; serialized writes; connection per operation; using statement ensures disposal
- **No background polling**: Persistence is event-driven; no periodic save without user action

## Trade-offs
- **Positive**: Versioned schemas prevent migration failures; SQLite provides robust vocabulary management; JSON is human-readable for config; import/export for backup/migration; offline-first; no cloud dependency
- **Negative**: SQLite adds a dependency (mitigated by embedded nuget package); JSON configuration requires schema management; two different storage mechanisms means two backup procedures
- **Risk**: SQLite database corruption; mitigated by WAL mode and regular backup on export

## Consequences
- **Positive**: Configuration survives upgrades via migration; vocabulary is queryable; import/export enables backup; local-first; no privacy concerns; predictable storage locations
- **Negative**: Users must manage two file types for backup; initial run creates files; migration edge cases must be handled

## References
- System.Text.Json for .NET 10
- Microsoft.Data.Sqlite NuGet package
- %APPDATA% and %LOCALAPPDATA% directory conventions
- Previous TypeRight configuration migration failures and data loss incidents