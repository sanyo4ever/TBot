# TBot 3.4.6b Stability Implementation Plan

> **For agentic workers:** Use test-driven implementation and independent final review. The controller coordinates independent domains in isolated clones and integrates only verified commits.

**Goal:** Release focused algorithm reliability fixes from the three community fork audits without importing their broad behavior or UI changes.

**Architecture:** Keep our C# scheduler/workers and pinned Go submodule. Make independent report/snapshot, single-discovery, and lifecycle/lock corrections, then integrate on release/3.4.6b.

**Tech Stack:** Existing .NET10, Go1.23.1 and Python release policy; no new production dependencies.

**Spec:** docs/superpowers/specs/2026-10-23-stability-letter-b-design.md

## Constraints and review focus

- No game/Telegram/login or current client execution; no private data in source.
- Retain all 3.4.6a fixes and protocol denial cases; no whole donor files/branches.
- Incomplete reports must not become safe targets; sender bonuses must not come from destination.
- Replacement snapshots must preserve earlier updates; per-celestial limits must not leak between lifeforms.
- A discovery response lost after acceptance must not trigger a second POST; old matching fleets do not prove a new dispatch.
- Settings failures must not leave reload locked or create two file locks; repeated shutdown signals must not throw.
- Root filesystem evidence: D:/TBOT/release-3.4.6b; donor audit reports: D:/TBOT/fork-audit-2026-10-23.

## Task 1: reports and Brain data correctness

Files: TBot.Ogame.Infrastructure/Models/EspionageReport.cs, affected current Brain workers, focused TBot.Tests files. Preserve public interfaces where possible.

- [ ] Create isolated clone from b1d96156 and record baseline test result.
- [ ] Add literal report null/zero/positive/negative/unknown cases; demonstrate expected failure.
- [ ] Add real worker fixtures returning replacement celestial objects and distinct sender/destination cargo bonuses; cover per-planet lifeform limits.
- [ ] Implement only failing behavior, run focused and full C# suites, commit with audit source attribution.

## Task 2: single-discovery Go protocol

Files: pkg/wrapper discovery paths and focused Go tests; no bulk-system API or dependency changes.

- [ ] Create isolated clone from52d5284; read source and donor protocol fixtures.
- [ ] Add transport fixtures for page token changes, invalid mission, missing token, internal302/new fleet, old fleet, login/external redirect, rejection, malformed response and one-POST limit; observe RED.
- [ ] Implement minimal single-discovery safeguards, preserve other protocol validators, run full Go suite and build.
- [ ] Commit MIT donor provenance and exact tests/limitations.

## Task 3: lock and shutdown reliability

Files: TBot.Common/Settings/SettingsService.cs, narrowly necessary settings reload/lifecycle files, TBot/Program.cs, focused tests.

- [ ] Create isolated clone fromb1d96156; reproduce concurrent lock-publication/reload failure and repeated cancellation behavior.
- [ ] Implement deterministic file lock ownership and guaranteed release without forced unlock or worker overlap; signal shutdown idempotently and set cancel handling appropriately.
- [ ] Run focused and complete C# suites; commit small independent changes.

## Task 4: integration and C# discovery no-replay

Files: TBot.Ogame.Infrastructure/OgameService.cs plus transport regression tests, release metadata/notes and Go gitlink.

- [ ] Integrate Tasks1/3 after inspecting every diff; resolve only real overlap.
- [ ] Add real OgameService fixture: failed or ambiguous single-discovery response causes exactly one transport POST; retain appropriate read retries and error information. Observe RED, implement no-replay boundary, GREEN.
- [ ] Verify combined Go/C#/Python/actionlint; independently review all final diffs, fix confirmed blockers with RED/GREEN.
- [ ] Publish Go PR and wait CI, merge ownmain; pin exact merge SHA in TBot.
- [ ] Update revisionb, .NET metadata and docs/releases/3.4.6b.md with actual final counts/limits; publish TBot PR, wait all CI, merge ownmain.
- [ ] Run manual3.4.6b release, validate seven assets and hashes, publish draft, prepare separate local folder with verified private config copies; never start or stop clients.

Integration ruling: the current user selected stability and already authorized our release workflow; ordinary reversible branch/code/test work proceeds without another permission loop. Major new behavior discovered during implementation is deferred rather than silently expanding this patch.
