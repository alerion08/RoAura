# AGENTS.md

## Purpose

This file is the compact source of working rules for AI-assisted development of RoAura. Read it before changing code. Do not reconstruct project decisions from chat history when the repository documents answer the question.

## Sources of truth

Read only the documents relevant to the task:

1. `docs/PROJECT_STATE.md` — current version, branches, active work and known risks.
2. `docs/UI_SPEC.md` — interface and overlay behavior.
3. `docs/REGRESSION_CHECKLIST.md` — functionality that must not break.
4. `README.md` and `GUIDE_RU.md` / `GUIDE_EN.md` — public behavior and setup.
5. `RELEASE_NOTES.md` — released and prepared changes.

The current code and tests override stale prose. If implementation and documentation disagree, report the mismatch and update the relevant document in the same change.

## Scope discipline

- Work only on the subsystem requested by the user.
- Do not refactor unrelated code or redesign adjacent features without approval.
- Preserve existing functionality and user settings unless a migration is explicitly required.
- Record newly discovered out-of-scope problems instead of silently expanding the task.
- Never merge a pull request, create a tag or publish a GitHub Release without explicit user approval.
- Never commit personal `settings.json`, profiles, Recovery data, Market caches, logs or captured traffic.

## Efficient workflow

- Use one primary agent by default. Do not create subagents unless the user explicitly requests delegation or the task has genuinely independent research tracks.
- Inspect the smallest relevant file set; do not reread the entire repository or replay chat history.
- Group 3–7 related changes into one coherent iteration.
- Do not trigger remote CI after every micro-edit. Finish the scoped block, review the diff, run local checks when possible, then push once.
- Prefer one test archive per completed block.
- Keep user-facing status updates concise and report only material decisions, risks and results.

Model choice is controlled outside the repository. Use an economical model/reasoning level for mechanical edits and a stronger one for packet capture, window management, settings migration, concurrency and release work.

## Branches and releases

- `main` is the stable release branch. Current published application baseline is 3.3.0, released from `7d70b173b7c10cc4ab7ae3f268ff89cb9474e19e` through PR #10.
- Start new implementation work from current `main` in a task-specific branch. `feature/roquestie-mvp` is merged; older redesign/integration branches and PR #5 are historical, not current development targets.
- `research/quest-database` is independent data work; consuming a new snapshot requires explicit validation and must not silently replace released data.
- Development or integration work must not be pushed directly to `main`.
- A version change merged into `main` can create a tag and public release through GitHub Actions. Treat any merge to `main` as release-sensitive. Future releases require explicit user approval.

## Mandatory post-release documentation gate

A release task is NOT complete until the repository documents describe the actual published state. Do this immediately after publication, before the final completion report; do not defer it to a later user request.

1. Verify the public release/tag, released commit, final workflow result and all expected assets.
2. Update the CURRENT summary at the top of `docs/PROJECT_STATE.md`: version, released commit, branch status, shipped scope, validation evidence, remaining manual checks and next-task starting point.
3. Update `docs/UI_SPEC.md`, `docs/REGRESSION_CHECKLIST.md` and these branch instructions wherever current statements became stale. Keep dated history clearly labeled as history; replace pending release/CI claims with verified outcomes for the completed release.
4. Keep `README.md`, RU/EN guides and release notes consistent with shipped behavior/version. Do not claim unperformed manual acceptance.
5. Deliver the documentation update to `main` through the authorized documentation review path. A branch-only update is not a completed current-state update. Documentation-only maintenance must not bump the version, recreate a tag, replace assets or publish another release; use `[skip ci]` for its commit/merge to avoid rerunning release publication.
6. Re-read the resulting documents on `main` and verify version/branch/validation consistency. If blocked, explicitly report that documentation remains incomplete; never report the release task fully finished.

## Implementation constraints

- Target: Windows x64, .NET 8, WPF.
- Npcap is the only external runtime dependency.
- Preserve Russian and English localization parity.
- Preserve compatibility with existing settings and named presets.
- Overlay windows must not steal focus or block Alt, Ctrl or Shift.
- Do not restore cross-process ownership through `GWL_HWNDPARENT`.
- Coordinates are relative to the Ragnarok client and must account for per-monitor DPI.
- Packet parsing, connection discovery, overlay Z-order, global input hooks and settings persistence are high-risk areas and require focused regression testing.
- Keep version metadata centralized in `RoAura.csproj`; `app.manifest` must match and is checked by the build.

## Required checks

For a normal code change:

1. Review the diff for unrelated edits and private data.
2. Run the smallest relevant tests.
3. For a completed integration block, run:
   `dotnet test .\RoAura.Tests\RoAura.Tests.csproj -c Release`
4. For a testable portable package, run:
   `.\build-release.ps1`
5. Use `docs/REGRESSION_CHECKLIST.md` for affected functionality.
6. Update `docs/PROJECT_STATE.md` when branch state, known risks or the active task changes.
7. Update `RELEASE_NOTES.md` only for user-visible behavior intended for a release.

Automated tests do not replace manual testing inside Ragnarok for Npcap capture, relog/reconnect, overlays, hotkeys, fullscreen behavior and multiple clients.

## Completion report

State:

- what changed;
- what was tested;
- what still requires manual testing;
- branch and commit;
- whether an artifact was produced;
- whether anything remains blocked.

Do not describe a build as release-ready while required manual checks remain incomplete.
