# Work tracking

[Back to README](../README.md) · [Contribution workflow](../CONTRIBUTING.md) · [Issues](https://github.com/NahuelGenchi/kirbys-dead-land/issues) · [Milestones](https://github.com/NahuelGenchi/kirbys-dead-land/milestones) · [PRs](https://github.com/NahuelGenchi/kirbys-dead-land/pulls)

## Sources of truth

| Record | Owns |
| :--- | :--- |
| Issue | Scope, completion criteria, dependencies, decisions, blockers, outcome |
| Milestone | A coherent project outcome; no invented due dates |
| Pull request | A focused diff, closing issue reference, milestone, and delivery record |
| Documentation | Reusable project knowledge, not duplicate progress reports |

Track all meaningful work: features, bugs, enhancements, docs, research, releases and repository settings. Reuse an existing issue when the scope already fits.

## Milestones

| Key | Exact title | Scope |
| :--- | :--- | :--- |
| M0 | M0 — Project foundation | Repository import, documentation, agent instructions, and delivery workflow |
| M1 | M1 — Playable mod | Kirby playable in Dead Cells with original-ROM gameplay, plus play-test defects and fixes |
| M2 | M2 — Gameplay coverage | Measured parity across host enemies, hazards, items, bosses, and biomes |
| M3 | M3 — Release | Packaging and publishing without ROM, game files, or generated asset packs |

The first issue is #1 (M0). Later milestones may be created deliberately; assign them directly in GitHub.

## Issue and PR conventions

The work-item form requires a work type, milestone, scope, completion criteria, and constraints. Blank issues are disabled in the UI. API-created issues should include a line such as:

```text
Milestone: M1 — Playable mod
```

Issue and PR titles may start with a type prefix (`feat:`, `fix:`, `enh:`, `docs:`, `research:`, `ops:`), which the workflow turns into a label. Name branches `<type>/<issue>-<slug>`. Every PR must include a standalone closing reference to its scoped issue:

```text
Closes #123
```

Use one closing line per completed issue and `Refs #123` for unfinished parent or related work. Keep closing issues within the PR's milestone.

## Metadata-only automation

[`.github/workflows/project-tracking.yml`](../.github/workflows/project-tracking.yml) manages metadata, not software quality. It creates the milestones and tracking labels idempotently, processes issues before PRs, and fills missing milestones. An issue's explicit GitHub milestone wins; otherwise the form or `Milestone:` line is used, and unclassified issues fall back to M1.

PRs inherit a missing milestone from their first valid closing issue. PRs without a valid closing issue, or with a milestone mismatch, get the `needs-tracking` label; fix the body or milestone to clear it. The workflow does not comment.

The job uses inline Node.js and the repository's `GITHUB_TOKEN` with only issue and PR write permissions. It has no checkout, third-party action, build, or test. PR text is parsed as data, never executed. To repair metadata after a missed run, use **Actions → Project tracking → Run workflow**.

## Enforcement boundary

**The workflow does not prevent direct pushes or block merges.** PR-only delivery is project policy; server-side enforcement needs a branch ruleset configured by the owner (require a PR for `main`, block force pushes and deletion). Record that setup in its own operations issue.
