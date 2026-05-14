# Instruction: Release Workflow

## Intent

Cut a new MediatorLite release: bump semver on the three shipped packages, update the changelog (if present), merge to `main`, create a `vX.Y.Z` git tag, let the publish workflow build + test + pack + push to NuGet.org, and verify no benchmark regression slipped in. The process is driven by [.github/workflows/publish.yml](.github/workflows/publish.yml) — **the tag is the release trigger**.

## When to use

- Shipping a bug fix (patch), a backwards-compatible feature (minor), or a breaking change (major).
- Re-publishing after a pulled release (use `--skip-duplicate`; the workflow already passes it).

## Agent ownership

- **Primary:** `devops`.
- **Sign-off:** orchestrator / human user for major or minor bumps (public API changes require a human in the loop, per [.claude/agents/orchestrator.md](.claude/agents/orchestrator.md)).
- **Benchmark check:** `devops` compares the latest benchmark run vs. the previous release line.

## Inputs / Preconditions

- `main` is green on [.github/workflows/ci.yml](.github/workflows/ci.yml) (Build & Test + Code Quality).
- The most recent benchmark run on `main` shows no regression > 10% vs. the previous tagged release; see [write-and-run-benchmarks.md](write-and-run-benchmarks.md).
- Three `.csproj` files host the shipped packages — version bumps must be applied to each:
  - `src/MediatorLite/MediatorLite.csproj`
  - `src/MediatorLite.Abstractions/MediatorLite.Abstractions.csproj`
  - `src/MediatorLite.SourceGeneration/MediatorLite.SourceGeneration.csproj`
- `secrets.NUGET_API_KEY` is configured on the repository (used by the publish workflow).

## Numbered steps

1. **Choose the version bump** (semver):
   - **Patch** (`X.Y.Z+1`) — bug fixes, doc-only changes, internal refactors with no public API or behavioural delta.
   - **Minor** (`X.Y+1.0`) — new `IRequest` shapes, new attributes, new `AddGenerated*` method, new opt-in behaviour; fully backwards-compatible.
   - **Major** (`X+1.0.0`) — any break to the public API of `IMediator`, `AddMediatorLite()`, the attribute surface, or observable generator output.

2. **Bump `<Version>` in each `.csproj`**. If the packages use a shared `Directory.Build.props` `<Version>` property, bump it there once; otherwise bump all three individually. Confirm by grepping for the old version:

   ```powershell
   Select-String -Path src\**\*.csproj -Pattern '<Version>' -SimpleMatch
   ```

3. **Update the changelog (if present).** If `CHANGELOG.md` exists at the repo root, prepend a new section with the version, date, and a bullet list grouped by `Added`, `Changed`, `Fixed`, `Breaking`. If it does not exist, skip — the GitHub Release is auto-generated from commits by the publish workflow:

   ```94:100:.github/workflows/publish.yml
       - name: Create GitHub Release
         uses: softprops/action-gh-release@v1
         if: startsWith(github.ref, 'refs/tags/') || github.event_name == 'workflow_dispatch'
         with:
           files: ./artifacts/*.nupkg
           generate_release_notes: true
           tag_name: ${{ steps.release_tag.outputs.TAG_NAME }}
   ```

4. **Verify benchmarks locally** on the release candidate commit:

   ```powershell
   dotnet run -c Release --project tests/MediatorLite.Benchmarks -- --filter '*' --exporters json markdown --memory
   ```

   Compare the summary to the previous release's snapshot (available in [docs/benchmarks.md](docs/benchmarks.md) or the prior release's artifact). No regression > 10% mean time / > 5% allocations without a documented reason.

5. **Open a release PR** titled `chore(release): vX.Y.Z`. Required content:
   - Version bump diff on the three `.csproj`s.
   - Changelog delta (if applicable).
   - Benchmark comment from CI attached to the PR.
   - Link to any lessons or memories shipped in this release.

6. **Merge to `main`** once the CI workflow is green and `code-reviewer` has approved.

7. **Create and push the tag** from the merge commit on `main`. The publish workflow keys off tags of the form `v*.*.*`:

   ```6:10:.github/workflows/publish.yml
   on:
     push:
       tags:
         - 'v*.*.*'  # Triggers on version tags like v1.0.0
     workflow_dispatch:  # Allows manual trigger
   ```

   Commands:

   ```powershell
   git checkout main
   git pull
   git tag -a v1.2.3 -m "MediatorLite v1.2.3"
   git push origin v1.2.3
   ```

8. **Watch the publish workflow**. It performs, in order:
   - Restore → build with `/p:Version=<X.Y.Z>` → run the full test suite.
   - `dotnet pack` on each of the three projects into `./artifacts/*.nupkg`.
   - Upload the artifact (`nuget-packages`).
   - `dotnet nuget push ... --skip-duplicate` for each package to NuGet.org.
   - Create the GitHub Release and attach the `.nupkg` files.

   Expected: workflow exit code `0`, three packages visible on nuget.org at the new version within a few minutes.

9. **Post-publish verification** (smoke test from a clean working directory):

   ```powershell
   dotnet new console -n ReleaseSmoke -o /tmp/ReleaseSmoke
   cd /tmp/ReleaseSmoke
   dotnet add package MediatorLite --version 1.2.3
   dotnet add package MediatorLite.SourceGeneration --version 1.2.3
   dotnet build
   ```

   Build should succeed. If the new packages cannot be restored, investigate the NuGet.org index lag or a `--skip-duplicate` false positive.

## Validation / Acceptance

- Tag `vX.Y.Z` exists on the release commit and is pushed to `origin`.
- All three packages are visible on NuGet.org at version `X.Y.Z`.
- GitHub Release at [github.com/.../releases/tag/vX.Y.Z](https://github.com/ABDevStudio/MediatorLite/releases) lists all three `.nupkg` assets.
- CI for the release commit on `main` is green; the publish workflow run for the tag is green.
- Benchmarks on `main` at the release commit show no unacknowledged regression.

## Handoff / Exit criteria

- `devops` confirms the release on NuGet.org and updates any downstream consumer instructions if the public API changed.
- Orchestrator closes the release session and records a `.github/Memories/` entry if the release introduced any operational nuance (e.g. a pinned `<RollForward>` behavior, a hotfix retrospective).

## Related rules, skills, instructions

- Workflow: [.github/workflows/publish.yml](.github/workflows/publish.yml).
- CI gate: [.github/workflows/ci.yml](.github/workflows/ci.yml).
- Benchmarks: [write-and-run-benchmarks.md](write-and-run-benchmarks.md).
- Package roots: [src/MediatorLite/MediatorLite.csproj](src/MediatorLite/MediatorLite.csproj), [src/MediatorLite.Abstractions/MediatorLite.Abstractions.csproj](src/MediatorLite.Abstractions/MediatorLite.Abstractions.csproj), [src/MediatorLite.SourceGeneration/MediatorLite.SourceGeneration.csproj](src/MediatorLite.SourceGeneration/MediatorLite.SourceGeneration.csproj).
- Conventions: [Directory.Build.props](Directory.Build.props), [.claude/rules/00-project-conventions.mdc](.claude/rules/00-project-conventions.mdc).
- Agent: [.claude/agents/orchestrator.md](.claude/agents/orchestrator.md).
- Related instructions: [bug-fix-workflow.md](bug-fix-workflow.md), [orchestration-playbook.md](orchestration-playbook.md).
