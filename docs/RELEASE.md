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
