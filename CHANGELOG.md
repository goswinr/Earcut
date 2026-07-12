# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

The first three digits of the version number (e.g. `3.0.2`) correspond to the original Mapbox Earcut version,
while the last digits indicate the release number of this F# port.

## [Unreleased]


## [3.2.31] - 2026-07-12
### Changed
- Target .NET Standard 2.0 only, replacing the .NET Framework 4.7.2 and .NET 6.0 targets
- Port of Mapbox Earcut version 3.2.3 (upstream versions 3.1.0 through 3.2.3)
- Much faster hole elimination via a block-bbox spatial index for hole bridge search
- Z-order sorting switched from linked-list merge sort to insertion/radix sort over arrays
- Simplified `earcutLinked` ear-slicing loop and `isEar`/`isEarHashed` checks
- Better filtering of collinear/coincident points for pathological cases
- Fixed a rare self-tangency issue and an edge case with coincident holes
- Triangulations can differ from previous releases (equally valid results, verified element-for-element identical to the reference JS implementation)
- Breaking: the module now keeps reusable scratch state at module level (mirroring the upstream JS), so calls into the Earcut module are no longer thread-safe
### Added
- `refine` function: optional Delaunay refinement post-pass (Lawson flips) that improves triangle quality in place while preserving the polygon boundary, holes and triangle count
- MVT regression test suite over 119,680 real-world polygons and new upstream test fixtures
- MVT benchmarks (`Test/bench/bench-tiles.js` and `Test/bench/bench-refine.js`)

## [3.0.24] - 2026-06-17
### Fixed
- Fix bad index in `earcutTriangles`


## [3.0.23] - 2026-06-15
### Changed
- rename `earcut_xy` to `earcutTrianglesFromMembersxy`
- rename `earcut_XY` to `earcutTrianglesFromMembersXY`
### Added
- `earcutTriangles`


## [3.0.22] - 2026-03-15
### Added
- `validate` function to verify earcut input data before triangulation

## [3.0.21] - 2026-03-11
### Added
- earcut_XY function for point objects with X/Y properties
- earcut_xy function for point objects with x/y properties

## [3.0.2-r3] - 2025-11-08
### Fixed
- Image links in README.md

## [3.0.2-r2] - 2025-11-08
### Added
- More test documentation in README.md


## [3.0.2-r1] - 2025-11-08
### Changed
- First release of Port of Mapbox Earcut version 3.0.2 to F#



[3.2.31]: https://github.com/goswinr/Earcut/compare/3.0.24...3.2.31
[3.0.24]: https://github.com/goswinr/Earcut/compare/3.0.23...3.0.24
[3.0.23]: https://github.com/goswinr/Earcut/compare/3.0.22...3.0.23
[3.0.22]: https://github.com/goswinr/Earcut/compare/3.0.21...3.0.22
[3.0.21]: https://github.com/goswinr/Earcut/compare/3.0.2-r3...3.0.21
[3.0.2-r3]: https://github.com/goswinr/Earcut/compare/3.0.2-r2...3.0.2-r3
[3.0.2-r2]: https://github.com/goswinr/Earcut/compare/3.0.2-r1...3.0.2-r2
[3.0.2-r1]: https://github.com/goswinr/Earcut/releases/tag/3.0.2-r1
