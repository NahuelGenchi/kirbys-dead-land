# Releases

[Back to README](../README.md) · [Third-party notices](../THIRD_PARTY_NOTICES.md) · [Work tracking](TRACKING.md)

A release is a source archive built from one commit. It holds no ROM, Dead Cells
file, binary, save, state, recording or generated pack; players build everything
with `scripts/install.py` (see [Install](../README.md#install)).

## Building the archive

```sh
python3 scripts/package_release.py --ref v0.1.0
```

The script reads tracked files from the commit with git (never the working tree),
leaves out the repository workflow files (`.github/`, `AGENTS.md`, `CLAUDE.md`,
`.gitignore`), and writes `dist/kirbys-dead-land-<version>.tar.gz` and its
`.sha256`. The version comes from `mod.json` at that commit. Executable bits are
kept and file times are the commit time.

Before writing anything it runs the release lint, which fails on:

- ROM, save, state, game, binary, archive, image, audio, video and dump suffixes
  (`.nds`, `.sav`, `.state`, `.pak`, `.dat`, `.hdll`, `.dll`, `.so`, `.png`, `.wav`,
  `.mp4`, `.dmp` and others in `FORBIDDEN_SUFFIXES`);
- any file inside a private data folder (`evidence`, `lab`, `rom`, `oracle`,
  `research`, `repros`, `outfit-build`, `ability-icons`, build outputs and others);
- files over 1 MiB or with NUL bytes;
- key-like strings (GitHub, OpenAI-style, AWS, Slack and fal keys, private key
  blocks).

`tests/test_release_package.py` covers each rule and checks that the current
commit passes.

## GPL

The archive is the complete source of this project, including the melon-rs and
melonDS patches. Release archives attach no built bridge; anyone who distributes
one must offer the corresponding source (see the
[notices](../THIRD_PARTY_NOTICES.md#modified-gpl-source)).

## Going public

Checked on 2026-10-20 (#216) before the repository was made public: a credential
scan of all 354 commits (main, every branch still on GitHub and every pull
request's `refs/pull/*/head`, 1544 file versions), commit and tag messages, all 215
issues and pull requests with their comments, edit histories and reviews, and the
release notes found no key, token, password, private key or credential file. The
only matches are the synthetic strings in `tests/test_release_package.py`. The
repository has no Actions secrets, webhooks or deploy keys. Owner-specific
absolute paths were replaced with `~` or `$HOME`. Five early commits carry the
owner's email address, which their older public repositories already show; the
history was not rewritten.

The project-tracking workflow is safe to run on forks' pull requests: it uses
`pull_request_target` with issue and pull request write access only, checks out no
code and reads issue and pull request text as data, never as a command.

After switching the visibility (**Settings → General → Danger Zone → Change
visibility**), turn on in **Settings → Advanced Security**: private vulnerability
reporting (used by [`SECURITY.md`](../SECURITY.md)), Dependabot alerts, secret
scanning and push protection. Then add a branch ruleset for `main` in **Settings →
Rules → Rulesets** that requires a pull request and blocks force pushes and
deletion (see [work tracking](TRACKING.md#enforcement-boundary)). Public
repositories run standard Actions runners at no cost, so the tracking workflow,
stopped by billing on the private repository, runs again.

## v0.1.0 (2026-10-20, pre-release)

The first release, tagged `v0.1.0` and published as a GitHub pre-release on the
private repository, with `kirbys-dead-land-0.1.0.tar.gz` and its `.sha256` attached.

- Linux x64 only. Needs Steam Dead Cells (tested with v35, build 23762174) and the
  player's own Kirby Super Star Ultra USA revision 0 ROM (SHA-256
  `b2573313a582c8dfaf2d93834a670c1ca8f389e5daa744e2dbe1a4ca903f3027`).
- Verified install: the archive installed from an empty data root and launched
  once into a playable Kirby run (#210, see
  [Runtime launch](RUNTIME_LAUNCH.md#clean-install-from-a-release-archive-210)).
- Gameplay verified live is listed under
  [Verified behavior](../README.md#verified-behavior); complete parity for every
  host interaction is not established. Known limits and deliberate deviations are in
  [Gameplay fidelity](GAMEPLAY_FIDELITY.md) and
  [Compatibility and limits](../README.md#compatibility-and-limits).
