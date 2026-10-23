# Independent fork implementation plan

Keep the existing upstream PR branches intact. Bootstrap `main` in both user-owned forks from the exact tested PR heads. TBot builds the locally pinned Go module; no additional library fork or module import-path rename is needed.

- [ ] Establish `sanyo4ever/ogame.mod:main`, identify the fork in README, and point its legacy release configuration at the fork and custom ogamed-tbot command. Retain upstream history and license.
- [ ] Point TBot's submodule at that fork/main while retaining an exact gitlink. Identify our release channel in metadata and documentation. Replace duplicated workflows with one tested seven-platform matrix: normal pushes/PRs produce artifacts, manual dispatch from main creates a draft prerelease only after all tests/builds succeed. Validate fork-specific version input and use numeric assembly versions. Package source provenance, Go license and checksums.
- [ ] Verify workflow syntax, version validation, source tests and actual GitHub CI; independently review the changes. Publish each new main branch, make it the fork's default, preserve upstream PR branches and leave the installed client untouched. Do not create the first release in this setup task.

Decisions: reuse the existing isolated checkouts; `main` is the independent maintenance branch. Use `3.4.7-sanyo.N` versions and `sanyo-v3.4.7-sanyo.N` tags to distinguish these builds from upstream. Only release jobs receive contents-write permissions. Future fixes use short branches and PRs against our main; upstream updates are reviewed before importing. No live OGame or Telegram request is part of validation.
