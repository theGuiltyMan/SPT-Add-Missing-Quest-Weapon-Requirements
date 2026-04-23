# Contributing

Thanks for considering a contribution. This is a server-side SPT 4.x mod written in C# (.NET 9).

## Getting started

```bash
git clone <your-fork>
cd addmissingquestrequirements
dotnet build -c Release
dotnet test --filter "FullyQualifiedName!~Integration"
```

Unit tests are hermetic and run offline in a few seconds. Integration tests need an SPT database slice — see `README.md` § Build from source. When no slice is reachable the integration tests short-circuit to a no-op pass (so `dotnet test` stays green on a clean clone); run them against a real slice before shipping changes that touch categorization or the patcher.

## Project layout

- `AddMissingQuestRequirements/` — the mod. Three pipeline phases under `Pipeline/`: `Override/` reads user config, `Weapon/` + `Attachment/` categorize items, `Quest/` patches quest conditions. SPT wiring in `Spt/AddMissingQuestRequirementsLoader.cs`.
- `AddMissingQuestRequirements.Inspector/` — standalone CLI that runs the same pipeline against an exported DB slice and emits a JSON + HTML report. Two modes: one-shot and serve + watch. Run via `dotnet run --project AddMissingQuestRequirements.Inspector [-- serve]`, or the Windows wrappers in `tools/`.
- `AddMissingQuestRequirements.Tests/` — xUnit + FluentAssertions. Integration tests are opt-in via `[Trait("Category", "Integration")]`.
- `SptDbExporter/` — small utility that dumps the SPT item/quest/locale tables to JSON so the Inspector can run without booting a server. `SptDbExporter/export/` is gitignored; check it in only for tracked test fixtures.
- `config/` — the default `config.jsonc` + `MissingQuestWeapons/*.jsonc` shipped to end-users.
- `tools/` — author workflow scripts (Windows-only; not required to build or run the mod).

## Code style

See `CLAUDE.md` for the full checklist. Short version:

- Always use braces, even on single-line blocks.
- No expression-bodied members for anything with logic.
- `sealed` by default. `init`-only properties for data classes. `var` where the type is obvious.
- No comments unless a reader would otherwise be confused. Avoid "TODO" — if it's a known issue, open an issue.

## Adding a new rule condition

`Pipeline/Rules/RuleConditions/` has one file per condition key (`HasAncestorCondition`, `NameMatchesCondition`, etc). To add a new key:

1. Implement `IRuleCondition`.
2. Register it in `ConditionFactory.Create`.
3. Decide whether it's a "core" condition (see `RuleCoreDetector`) — core conditions still fire even when an item has a `manualTypeOverrides` entry.
4. Add tests under `AddMissingQuestRequirements.Tests/Pipeline/Rules/`.

## Adding a new condition expander

Field-level expanders live under `Pipeline/Quest/` and implement `IConditionExpander`. Register the new expander in the `QuestPatcher` list in `AddMissingQuestRequirementsLoader.cs`. No changes to the patcher core loop required.

## Reporting bugs

Turn on `"debug": true` in `config/config.jsonc` and restart the server. Open an issue with:

- The offending quest / weapon / attachment (name + ID).
- Your `config.jsonc`, `WeaponOverrides.jsonc`, `QuestOverrides.jsonc`, `AttachmentOverrides.jsonc`.
- `AddMissingQuestRequirements-debug-report.json` + `.html` from next to the DLL.
- `AddMissingQuestRequirements.log` from next to the DLL.

## Before opening a PR

- `dotnet build -c Release` — must be 0 warnings, 0 errors.
- `dotnet test --filter "FullyQualifiedName!~Integration"` — must be green.
- If your change touches the pipeline, run the Inspector against the shipped config and eyeball the report for surprises.
