# Maintaining our TBot fork

The independent development/release line is `main` in [sanyo4ever/TBot](https://github.com/sanyo4ever/TBot) and [sanyo4ever/ogame.mod](https://github.com/sanyo4ever/ogame.mod). It starts from the tested changes proposed in [TBot #62](https://github.com/0xE232FE/TBot/pull/62) and [ogame.mod #174](https://github.com/0xE232FE/ogame.mod/pull/174). Upstream history and attribution are retained; those PR branches remain available for the original owners.

## Development

```sh
git clone --recurse-submodules --branch main https://github.com/sanyo4ever/TBot.git
cd TBot
git switch -c fix/short-description
dotnet test TBot.sln -c Release
cd ogame
go test -count=1 ./...
go build ./...
```

Open changes against **our `main`**, review the diff and wait for CI before merging. No game credentials are required for these tests. The GitHub workflow builds and uploads ZIP artifacts for all seven supported platforms on pushes and pull requests.

For backend fixes, open a branch/PR in `sanyo4ever/ogame.mod`, merge it into our `main`, then update TBot's `ogame` gitlink in a separate change. The submodule URL and its suggested branch are ours; its checked-in SHA, rather than a moving branch tip, determines every build. Keep the original Go module path; it resolves to the submodule checkout during compilation.

```sh
git submodule sync --recursive
git -C ogame fetch origin main
git -C ogame checkout --detach origin/main
git add ogame
git commit -m "build: update pinned ogamed backend"
```

## Version policy

The public version is the reviewed author release plus a lowercase letter: `3.4.6a`, `3.4.6b`, and so on. `fork-version.json` records the upstream release, its commit, the source branch/commit used by our fork, and our revision letter. Our current source also includes reviewed develop-branch changes; the recorded source commit makes that distinction visible. The CLI rejects a release label that differs from this checked-in policy. .NET package versions use valid SemVer (`3.4.6-b` for this revision), while the window title, GitHub tag, package names and informational version display `3.4.6b`; numeric assembly/file versions remain `3.4.6`.

When the author publishes a new release, review its commits and merged PRs first. Decide what to import on an integration branch, run the regression suite, and only then update the recorded upstream version/commit and restart our letter at `a`. Do not update automatically or overwrite our main.

## Creating our releases

1. Ensure the intended TBot and Go fixes are committed on our `main` and CI is green.
2. Check that the version policy, application metadata and `docs/releases/VERSION.md` describe the reviewed change. Open [Actions / Fork CI and release](https://github.com/sanyo4ever/TBot/actions/workflows/build.yml), choose **Run workflow**, select **main**, and enter the reviewed version such as `3.4.6b`.
3. The workflow validates the version/branch, tests both projects, and builds seven self-contained packages. Only after every package succeeds does it atomically create a new tag at the built commit and create a **draft release** tagged `3.4.6b` in our repository. Any existing tag fails rather than being reused or replacing assets. If release creation fails after the tag was reserved, inspect that tag/run before retrying with a new version.
4. Review the draft's source commit, ZIPs and `SHA256SUMS`. Every package contains `BUILD-INFO.txt` with both source SHAs and toolchain versions. Perform any desired live-account check separately, then publish the draft from [our Releases page](https://github.com/sanyo4ever/TBot/releases).

Ordinary pushes, PRs and upstream tags do not create a release. A draft allows the owner to verify the artifacts before publishing. Automated tests/builds do not establish that every action works against the current live game. Installing a release locally is a separate explicit step; never copy private settings into a source commit or a public release archive.

## Upstream updates

Fetch updates from `0xE232FE/TBot:develop` and `0xE232FE/ogame.mod:develop-ogamed-tbot` into separate integration branches. Review and test them before merging into our `main`; resolve overlaps with our fixes. Do not use GitHub's discard/reset synchronization option on our main. No automatic upstream merge or scheduled dependency update is enabled.

The original maintainers can still merge our existing upstream PRs independently. Our builds and release schedule do not require their approval.
