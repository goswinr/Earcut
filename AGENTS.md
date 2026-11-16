# AGENTS.md


## What this is

Earcut is an F# (and Fable/JS) port of [Mapbox's Earcut](https://github.com/mapbox/earcut)
polygon triangulation library. All shippable code lives in a single file: [Earcut.fs](Earcut.fs).
The library has **no dependencies** and is published to NuGet as both a .NET library and a
Fable source package (the `.fs`/`.fsproj` sources are packed under `fable/` so Fable consumers
compile the F# directly to JS with their own Fable version).

The exact upstream commit the port corresponds to is linked in the header comment of `Earcut.fs`.
That link is the reference to diff against. When porting newer upstream commits, update the link
together with the status line in [README.md](README.md) and add a changelog entry.

Versioning: the first three digits (e.g. `3.0.2`) track the upstream Mapbox Earcut version;
trailing digits are the F# port's release number (`3.2.41` is the first release of upstream `3.2.4`).
See [CHANGELOG.md](CHANGELOG.md) - the build derives the package version from it via
`Ionide.KeepAChangelog.Tasks`, so add a changelog entry under a new version heading when releasing.
Until such a heading exists, local builds are still named after the last released version.

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
  `dimensions` explicitly (`earcut(vertices, holes, 2)`); `refine` takes `dim` explicitly too.
- [Test/test.fsx](Test/test.fsx) is the .NET port of the same tests, run with `dotnet fsi`. It
  `#load`s `Earcut.fs` directly, so it needs no prior build. It is not part of the Fable build;
  thread-based tests are wrapped in `#if !FABLE_COMPILER`. It adds two .NET-only tests: nested
  splits on a 256 KB stack and concurrent `earcut`/`refine` calls.

CI: [test.yml](.github/workflows/test.yml) runs only the JS suite (`dotnet fable --noCache` then
`node Test/test.js`), [build.yml](.github/workflows/build.yml) only compiles the .NET library.
Nothing in CI runs `Test/test.fsx`, so run it locally after any change to `Earcut.fs`.

To run a subset of the JS suite, filter by test name: `node --test-name-pattern="issue219" Test/test.js`.
The .NET script has no filter - to isolate one case, edit the `fixtureIds` loop or comment out
`runTest` calls.

Benchmarks live in [Test/bench/](Test/bench/) and are run by hand, each version in a fresh process:

```sh
node Test/bench/bench-tiles.js     # earcut over the MVT fixture (after dotnet fable)
node Test/bench/bench-refine.js    # refine over the MVT fixture
node Test/bench/bench-large.js     # large single rings
dotnet build -c Release            # the .fsx ports below load bin/Release/netstandard2.0/Earcut.dll
dotnet fsi Test/bench/bench-tiles.fsx
dotnet fsi Test/bench/bench-refine.fsx
dotnet fsi Test/bench/bench-large.fsx
```

The JS test and bench files carry a commented-out `// original JS version` import. To A/B against
upstream, clone it at the pinned commit into the gitignored `mapbox-earcut/` folder and swap the
import (the bench files already use the `../../mapbox-earcut/src/earcut.js` path).
[Test/bench/README.md](Test/bench/README.md) has the latest results against upstream and the
previous release, and how to rerun that comparison. Update it when a change affects performance.

In Claude Code on the web the container has Node but no .NET SDK, so
[.claude/hooks/session-start.sh](.claude/hooks/session-start.sh) installs SDK 10 into
`$HOME/.dotnet`, runs `dotnet tool restore`/`restore` and then `dotnet fable`. It runs
asynchronously, so at the very start of a session `dotnet` may not be on `PATH` yet - if a
command fails with `dotnet: command not found`, wait and retry rather than reinstalling.

## Tests and fixtures

- [Test/fixtures/](Test/fixtures/) holds GeoJSON-style polygon inputs (shared with upstream),
  many named after the original `issueNN` they reproduce.
- [Test/expected.json](Test/expected.json) holds, per fixture: expected triangle count,
  the allowed `deviation` tolerance, and a separate tolerance for rotated variants. Its content
  is kept identical to upstream's `test/expected.json`; copy it from upstream when porting rather
  than tuning values locally.
- [Test/bench/tiles-fixture.bin](Test/bench/tiles-fixture.bin) (119,680 real MVT polygons) is used
  by both test suites and the benchmarks; every polygon must triangulate with zero deviation.
- Each fixture is run at 0/90/180/270° rotations. Both suites pass completely, so treat any
  failure as a regression.
- Correctness is checked via `Earcut.deviation` (relative area mismatch between triangulation
  and polygon), not exact triangle equality, because the algorithm trades correctness for speed.
- Tests that do not come from upstream sit at the end of both files: the merge order of tied
  holes, `validate` and `earcutTriangles`. The inline wrappers `earcutTrianglesFromMembersxy/XY`
  are not emitted to JS, so only `test.fsx` covers them.
- `Test/draw-in-Rhino*.fsx` and `Test/test-and-draw-in-Rhino.fsx` are manual visual-debugging
  scripts for Rhino with hard-coded local paths. They are not part of either suite. The `FAIL`
  list at the top of the latter describes old runs inside Rhino, not expected failures.

## Architecture notes

The port deliberately mirrors the upstream JS line-for-line - keep that fidelity when fixing bugs
so it stays diffable against the reference implementation (linked at the top of `Earcut.fs`).
Key consequences for editing:

- The core works on a **circular doubly linked list** of `Node` records (`prev`/`next`), with a
  second `prevZ`/`nextZ` linkage for the z-order curve hash that accelerates ear-checking on
  larger polygons (`isEarHashed` vs `isEar`). `Node.z` doubles as the owning block index while
  holes are being merged.
- `Node` is a reference type compared by **identity**, not value. Use the custom operators
  `===`/`=!=` and the `isNull`/`notNull` helpers (built on `obj.ReferenceEquals` and
  `Unchecked.defaultof<Node>`) - not `=`/`null`/`Option`. This is how the JS `null`-as-sentinel
  pattern is reproduced. `Node` is a cyclic record, so structural `=` on it would recurse around
  the ring. The module-level `isNull` shadows the FSharp.Core one and only accepts a `Node`.
- JS `do…while` loops are translated to F# `while continueLoop do` with an explicit
  `continueLoop` flag and a body that runs once before the condition check. Early `return`,
  `continue` and `break` become flags too (`result`, `hasEarlyReturn`, ...). Preserve this shape.
- `earcutLinked` slices ears until a full lap finds none, then escalates: `filterPoints` for as
  long as it removes nodes (`filterPointsIfChanged`), then `cureLocalIntersections` once (`cured`),
  then `splitEarcut` as a last resort. `splitEarcut` does not recurse: it pushes both halves on
  `ws.splitStack`, which `earcut` drains in a loop. There is no `pass` parameter any more.
- Hole merging (`eliminateHoles`) uses a block-bbox index (`buildBlockIndex`, `indexSegment`,
  `growBlock`, `K` edges per block) so `findHoleBridge` can skip whole blocks.

### Scratch state: `Workspace`

Upstream keeps its reusable scratch buffers in module-level variables. The port keeps them all in
the `Workspace` record, cached per thread on .NET (`ThreadLocal`) and as a single instance under
Fable, and passes it down as `ws`. This is what makes `earcut` and `refine` thread-safe on .NET.

- When porting upstream code that adds module-level state, add a `Workspace` field instead. Never
  add a module-level `mutable`.
- State left behind by a call must be reset at the start of the next one (see `steiners.Clear()`,
  `ws.numBlocks <- 0`, the `gen` stamp in `refine`), as in upstream.

### How upstream idioms map to the port

- Optional and default parameters are always explicit: `earcut(vertices, holes, dimensions)`,
  `refine(triangles, coords, dim)`, `intersects(..., includeBoundary)`, and a full-ring
  `filterPoints(start)` is written `filterPoints(ws, start, start)`.
- Upstream's `removeNode` calls `growBlock` when `indexActive`. In the port that call lives in
  `filterPoints`, the only remover that runs while the index is active, so `removeNode` needs no `ws`.
- Typed arrays become `float[]` / `int[]` (`Uint8Array` and `Uint32Array` included), `Math.imul`
  becomes the `imul` helper (emitted as `Math.imul` under Fable, wrapping `*` on .NET).
- `queue.sort(compareXYSlope)` relies on the JS sort being stable. `List.Sort` on .NET is not, so
  `eliminateHoles` uses `Seq.sortWith` there and keeps `queue.Sort` only under `FABLE_COMPILER`.
- `#if FABLE_COMPILER...` blocks are the only place where the two targets differ. Keep them minimal.

### Array access: `Arr` and `ResizeArr`

Fable 5 compiles `arr.[i]` and `arr.[i] <- v` to bounds-checked `item()`/`setItem()` library
calls, which made `earcut` about 1.3x and `refine` about 2x slower than upstream JS. Fable has no
switch for this, so indexing goes through the internal `Arr` (arrays), `ILst` (`IList`) and
`ResizeArr` (`ResizeArray`) modules at the top of `Earcut.fs`:

- Write `Arr.getIdx i arr` and `Arr.setIdx i value arr` (same for `ILst` and `ResizeArr`), never
  `.[i]`, in any internal code. Also use `len` (`.Length` / `.Count`), `ResizeArr.lastIdx`,
  `ResizeArr.clear`, `ResizeArr.pop` and, for arrays of `Node`, `Arr.zeroCreateUndef` (emits
  `new Array(len)` in Fable Release). Numeric arrays just use `Array.zeroCreate`: Fable emits a
  zero-filled typed array for them anyway, so there is no "uninitialized" variant.
  Avoid `for x in arr`, which also compiles to `item()`.
- The helpers emit raw `a[i]` only when both `RELEASE` and a Fable JS/TS symbol are defined.
  `dotnet fable` defines `RELEASE` by default, `dotnet fable watch` and `-c Debug` do not and keep
  the checks. On .NET they are plain indexing with the usual bounds checks.
- Public `inline` functions (the two SRTP wrappers) cannot call these internal helpers and use
  plain indexing (marked with a TODO in the source).
- To verify after a change: `grep -c "item(" Earcut.fs.js` should only count the few cold call
  sites (error messages in `validate`, the first lines of `flatten`; currently 3), and the
  benchmarks should stay on par with upstream.

### Public API surface (all in `module Earcut`)

- `earcut(vertices, holeIndices, dimensions)` - the core entry point. Returns a `ResizeArray<int>`
  of indices into the **point** array (multiply by `dimensions` for the flat vertex array).
  `holeIndices` are point indices (not flat indices); pass `null` or `[||]` for no holes.
- `refine(triangles, coords, dim)` - optional Delaunay post-pass (Lawson flips) that mutates the
  triangle list in place, keeping the boundary, holes and triangle count.
- `earcutTrianglesFromMembersxy` / `earcutTrianglesFromMembersXY` - convenience wrappers using F#
  **statically resolved type parameters** so any object with `x`/`y` (or `X`/`Y`) members works
  (e.g. Euclid's `Polyline2D.Points`). They return a flat `float[]` of triangle coordinates.
- `earcutTriangles` - same idea but takes flat `IList<float>` boundary + holes (a `ResizeArray<float>`,
  `float[]`, ...). The wrappers above likewise take any `IList` of points and of holes.
- `validate(vertices, holeIndices, dimensions)` - throws `ArgumentException` on malformed input
  (NaN/Infinity, wrong length, bad hole ordering, holes larger than outer ring, etc.).
- `deviation(...)` - triangulation correctness metric. `flatten(data)` - GeoJSON-shaped nested
  array → `{| vertices; holes; dimensions |}`.

Everything else in the file is `internal`. `earcut` and `refine` do not validate: malformed input
(an empty hole, a hole index past the end, an odd coordinate count) throws
`NullReferenceException` or `IndexOutOfRangeException` on .NET. In Fable Release builds it
behaves like upstream instead (out-of-range reads give `undefined`), so nothing may be thrown.
`flatten` and the three `earcutTriangles*` wrappers skip empty rings.

### Known limit of .NET / JS parity

Upstream, the Fable build and the .NET build produce identical triangle indices on all fixtures
and on the MVT set. One case cannot be made identical: `compareXYSlope` returns 0 when the slope
difference is NaN, which happens for a single-point hole (Steiner point) or a hole of coincident
points. If such a hole shares its leftmost point with two or more holes of different slopes, the
comparator is not transitive and the result depends on the sort algorithm, in upstream too (it
then differs between JS engines). Do not assume index-for-index equality for that input.

## Project constraints

- Targets `netstandard2.0` only (since 3.2.31, replacing the earlier `net472;net6.0` targets).
- `LangVersion=preview`; warnings are strict: `WarningLevel 5`, `--warnon:3390` (XML docstrings)
  and `--warnon:1182` (unused variables), plus `FsDocsWarnOnMissingDocs`. Keep public members
  documented and avoid unused bindings or the build will warn.
- `FSharp.Core` is pinned low (6.0.7) and `Fable.Core` is only referenced under `FABLE_COMPILER`,
  both on purpose; Dependabot is told to ignore them. Do not bump them as part of other work.
- `bin/`, `obj/`, `fable_modules/` and the Fable output `Earcut.fs.js` are gitignored
  build/restore artifacts, not part of this library's sources.

## Docs and release

- [README.md](README.md) is the single source for the docs landing page and the NuGet readme:
  [docs.yml](.github/workflows/docs.yml) copies it over the stub `Docs/index.md` before running
  `fsdocs`, and [releaseNuget.yml](.github/workflows/releaseNuget.yml) rewrites its `<br>` tags
  for nuget.org. Edit `README.md`, never `Docs/index.md`. Everything else under `Docs/` is copied
  to the public docs site as-is.
- Mapbox Earcut is ISC licensed. Its notice lives in the "Third-party notices" section of
  [LICENSE.md](LICENSE.md), after the MIT license of the port itself. `LICENSE.md` is packed into
  the NuGet package, keep it there when changing the packaging.
- A release is a pushed git tag equal to the newest version heading in `CHANGELOG.md`. The release
  workflow runs the JS suite, builds, fails if tag and changelog version differ, then pushes to NuGet.
