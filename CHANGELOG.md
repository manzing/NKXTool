# Changelog

All notable changes to NKXTool are documented in this file.
This project follows [Semantic Versioning](https://semver.org/).

## [2.1.0] - 2026-11-21

### Added

- Reversible name encoding: characters Windows cannot store in a file name (`*`, `?`, `%`, ...) are written as `%XX` during extraction and decoded by `pack`, so archives are rebuilt with their original entry names. No sidecar name map is needed.
- `pack` restores the original entry names when packing a subfolder (`Samples`, `Resources`), keeping the container root consistent.
- Key database (`nklibs.db`): keys are collected from the libraries installed on the machine, can be refreshed from Native Instruments (`update --online`) or imported from `NativeAccess.xml` (`update -f`), and existing entries are kept when merging.
- Tests covering path encoding, name decoding on pack, long container root names and registry keys.

### Fixed

- Extraction of entry names that Windows cannot store.
- Reading container root names whose header exceeds 64 bytes (for example `Additional samples`).

### Changed

- Build artifacts (`bin/`, `obj/`) are no longer tracked by git.
- CI builds on `main`, on `v*` tags and on pull requests targeting `main`.

## [2.0.0] - 2026-11-06

- First release of the fully native engine.
