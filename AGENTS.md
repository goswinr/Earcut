# AGENTS.md


## What this is

Earcut is an F# (and Fable/JS) port of [Mapbox's Earcut](https://github.com/mapbox/earcut)
polygon triangulation library. All shippable code lives in a single file: [Earcut.fs](Earcut.fs).
The library has **no dependencies** and is published to NuGet as a both a .NET library and a
Fable source package (the `.fs`/`.fsproj` sources are packed under `fable/` so Fable consumers
compile the F# directly to JS).

Versioning: the first three digits (e.g. `3.0.2`) track the upstream Mapbox Earcut version;
trailing digits are the F# port's release number. See [CHANGELOG.md](CHANGELOG.md) - the build
derives the package version from it via `Ionide.KeepAChangelog.Tasks`, so add a changelog entry
under a new version heading when releasing.

Changelog gotcha: `Ionide.KeepAChangelog.Tasks` 0.3.3 crashes (`MSB4018` /
`ArgumentOutOfRangeException` in `parseUnreleasedText`) on a bullet with an indented
continuation line - it reads the indent as a nested sub-item. This fails `dotnet build` and
therefore `dotnet fable` too. Keep every changelog bullet on a single line; inline links are fine.

## Commands

```sh
dotnet build                       # build .NET (netstandard2.0); also runs GeneratePackageOnBuild
dotnet tool restore                # install pinned Fable + fsdocs tools (.config/dotnet-tools.json)
dotnet fable                       # transpile Earcut.fsproj to Earcut.fs.js (use --noCache in CI / when stale)
node Test/test.js                  # run the JS test suite against Earcut.fs.js
dotnet fsi Test/test.fsx           # run the .NET test suite
```

There are two parallel test suites; when porting an upstream test, add it to both:

- [Test/test.js](Test/test.js) is a hand-written port of upstream's `test/test.js` (`node:test`).
  It imports `Earcut.fs.js`, the gitignored Fable output of [Earcut.fs](Earcut.fs), so run
  `dotnet fable` first. Unlike upstream, `earcut` is a named export and always takes
  `dimensions` explicitly (`earcut(vertices, holes, 2)`).
- [Test/test.fsx](Test/test.fsx) is the .NET port of the same tests, run with `dotnet fsi`. It is
  not part of the Fable build; thread-based tests are wrapped in `#if !FABLE_COMPILER`.

CI ([.github/workflows/test.yml](.github/workflows/test.yml)) runs only the JS suite:
`dotnet fable --noCache` then `node Test/test.js`.

To run a subset of the JS suite, filter by test name: `node --test-name-pattern="issue219" Test/test.js`.
The .NET script has no filter - to isolate one case, edit the `fixtureIds` loop or comment out
`runTest` calls.

In Claude Code on the web the container has Node but no .NET SDK, so
[.claude/hooks/session-start.sh](.claude/hooks/session-start.sh) installs SDK 10 into
`$HOME/.dotnet`, runs `dotnet tool restore`/`restore` and then `dotnet fable`. It runs
asynchronously, so at the very start of a session `dotnet` may not be on `PATH` yet - if a
command fails with `dotnet: command not found`, wait and retry rather than reinstalling.

## Tests and fixtures

- [Test/fixtures/](Test/fixtures/) holds GeoJSON-style polygon inputs (shared with upstream),
  many named after the original `issueNN` they reproduce.
- [Test/expected.json](Test/expected.json) holds, per fixture: expected triangle count,
  the allowed `deviation` tolerance, and a separate tolerance for rotated variants.
- Each fixture is run at 0/90/180/270° rotations. Some rotated cases are known-failing and are
  documented in the comment block at the top of `test.fsx` - don't treat those as regressions.
- Correctness is checked via `Earcut.deviation` (relative area mismatch between triangulation
  and polygon), not exact triangle equality, because the algorithm trades correctness for speed.

## Architecture notes

The port deliberately mirrors the upstream JS line-for-line - keep that fidelity when fixing bugs
so it stays diffable against the reference implementation (linked at the top of `Earcut.fs`).
Key consequences for editing:

- The core works on a **circular doubly linked list** of `Node` records (`prev`/`next`), with a
  second `prevZ`/`nextZ` linkage for the z-order curve hash that accelerates ear-checking on
  larger polygons (`isEarHashed` vs `isEar`).
- `Node` is a reference type compared by **identity**, not value. Use the custom operators
  `===`/`=!=` and the `isNull`/`notNull` helpers (built on `obj.ReferenceEquals` and
  `Unchecked.defaultof<Node>`) - not `=`/`null`/`Option`. This is how the JS `null`-as-sentinel
  pattern is reproduced.
- JS `do…while` loops are translated to F# `while continueLoop do` with an explicit
  `continueLoop` flag and a body that runs once before the condition check. Preserve this shape.
- The triangulation escalates through `pass` 0→1→2 in `earcutLinked`: plain ear slicing,
  then `filterPoints`, then `cureLocalIntersections`, then `splitEarcut` as a last resort.

### Public API surface (all in `module Earcut`)

- `earcut(vertices, holeIndices, dimensions)` - the core entry point. Returns a `ResizeArray<int>`
  of indices into the **point** array (multiply by `dimensions` for the flat vertex array).
  `holeIndices` are point indices (not flat indices); pass `null` or `[||]` for no holes.
- `earcutTrianglesFromMembersxy` / `earcutTrianglesFromMembersXY` - convenience wrappers using F#
  **statically resolved type parameters** so any object with `x`/`y` (or `X`/`Y`) members works
  (e.g. Euclid's `Polyline2D.Points`). They return a flat `float[]` of triangle coordinates.
- `earcutTriangles` - same idea but takes flat `ResizeArray<float>` boundary + holes.
- `validate(vertices, holeIndices, dimensions)` - throws `ArgumentException` on malformed input
  (NaN/Infinity, wrong length, bad hole ordering, holes larger than outer ring, etc.).
- `deviation(...)` - triangulation correctness metric. `flatten(data)` - GeoJSON-shaped nested
  array → `{| vertices; holes; dimensions |}`.

## Project constraints

- Targets `netstandard2.0` only (since 3.2.31, replacing the earlier `net472;net6.0` targets).
- `LangVersion=preview`; warnings are strict: `WarningLevel 5`, `--warnon:3390` (XML docstrings)
  and `--warnon:1182` (unused variables), plus `FsDocsWarnOnMissingDocs`. Keep public members
  documented and avoid unused bindings or the build will warn.
- `bin/`, `obj/`, `fable_modules/` and the Fable output `Earcut.fs.js` are gitignored
  build/restore artifacts, not part of this library's sources.
