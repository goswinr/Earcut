# Benchmarks

Earcut is heavily optimized for its primary workload: triangulating polygons from
[Mapbox Vector Tiles](https://github.com/mapbox/vector-tile-spec) (MVT).
These benchmarks measure that workload and a few large single rings,
for the Fable/JS build, the .NET build and the original JS implementation.

## Results

Times are in milliseconds, lower is better.

| Build | Code |
|---|---|
| Original JS | [mapbox/earcut](https://github.com/mapbox/earcut) at [934e54d](https://github.com/mapbox/earcut/commit/934e54d8bed85387330b7acaa5d2f5ba30a460a3), the commit this port follows |
| 3.2.43 | this port, current release (same library code as 3.2.42) |
| 3.2.41 | this port, previous release |

### MVT fixture: 119,680 polygons, 1.9M vertices

| Build | `earcut` | `refine` |
|---|--:|--:|
| Original JS | 310 | 120 |
| Fable JS 3.2.43 | 300 | 108 |
| Fable JS 3.2.41 | 383 | 221 |
| .NET 3.2.43 | 282 | 76 |
| .NET 3.2.41 | 281 | 76 |

`earcut` is from `bench-tiles`, `refine` from `bench-refine`. The `refine` column is the time
of the refine pass alone, on top of `earcut`.

- Fable JS 3.2.43 is about 1.3 times faster than 3.2.41 for `earcut` and 2 times faster for
  `refine`, because array access no longer goes through Fable's bounds-checked helpers.
  It is now slightly faster than the original JS.
- .NET is unchanged between the two releases on this workload. It is about 1.1 times faster than
  the original JS for `earcut` and 1.6 times faster for `refine`.

### Large single rings (`bench-large`), per call

| Case | Original JS | Fable JS 3.2.43 | Fable JS 3.2.41 | .NET 3.2.43 | .NET 3.2.41 |
|---|--:|--:|--:|--:|--:|
| regular 10k-gon | 1.04 | 0.96 | 36.2 | 0.74 | 26.1 |
| regular 100k-gon | 7.9 | 7.3 | 3,903 | 6.7 | 2,806 |
| star 10k | 36.3 | 27.0 | 27.2 | 40.1 ¹ | 20.0 |
| random-radius 10k | 7.65 | 5.82 | 6.23 | 5.18 | 5.20 |
| collinear rectangle 10k | 114 | 34.8 | 35.4 | 26.9 | 26.5 |

- Strictly convex rings (the regular polygons) took quadratic time in 3.2.41. 3.2.42 ported the
  fix from [mapbox/earcut#212](https://github.com/mapbox/earcut/pull/212), and the 100k-gon is now
  400 to 500 times faster. The concave and collinear control cases are unchanged.
- The original JS is about 3 times slower than the Fable build on the collinear rectangle and
  1.3 times slower on the star, with identical output. The algorithm is the same, so this is
  probably V8 optimizing the two code shapes differently. It was not investigated further.

¹ This case is bimodal on .NET: 20 ms once the JIT has fully optimized the code (the minimum in
every run), about 50 ms before. With 3.2.43 the cases before it finish too quickly to warm up the
JIT; with 3.2.41 the slow 100k-gon has already done so.

### Same output

All builds print the same checksums and the same refine quality figures, so they produce
identical triangles. The one exception is 3.2.41 on the MVT fixture: both of its builds give one
triangle fewer (1,747,434) and a different checksum, because of the bug fixes in 3.2.42 (see the
[changelog](../../CHANGELOG.md)).

### Method

- Measured on 2026-11-17 on an AMD Ryzen 5 9600X with Windows 11, Node.js 26.12.1 and .NET 11.0.0.
- Both Fable builds are compiled with Fable 5.27.0, so only the library code differs.
  Compiled with Fable 5.16.0, the version pinned at the 3.2.41 tag, 3.2.41 takes 410 ms for
  `earcut` and 252 ms for `refine`.
- The .NET builds are `dotnet build -c Release` (netstandard2.0), run with `dotnet fsi`.
- Each benchmark ran 3 times per build, each time in a fresh process, interleaved across builds.
  Each process reports the median of 5 timed runs; the tables show the median of those 3.
- All benchmarks warm up before timing. `bench-refine.fsx` warms up for 5 passes instead of one,
  because the .NET JIT takes longer to settle on `refine`.

## Running

Run each benchmark in a fresh process:

```sh
dotnet fable                          # the JS benchmarks import the Fable output Earcut.fs.js
node Test/bench/bench-tiles.js        # earcut over the MVT fixture, with a per-zoom breakdown
node Test/bench/bench-refine.js       # refine over the MVT fixture, and triangle quality
node Test/bench/bench-large.js        # large single rings

dotnet build -c Release               # the .NET benchmarks load bin/Release/netstandard2.0/Earcut.dll
dotnet fsi Test/bench/bench-tiles.fsx
dotnet fsi Test/bench/bench-refine.fsx
dotnet fsi Test/bench/bench-large.fsx
```

### Against the original JS

Put the upstream code at the commit linked at the top of [Earcut.fs](../../Earcut.fs) into the
gitignored `mapbox-earcut/` folder in the repository root (only `src/earcut.js` and
`package.json` are needed). Then, in the JS benchmark, swap the `import` for the commented-out
`// original JS version` line above it. For `bench-tiles.js` and `bench-refine.js`, do the same
in `tiles-fixture.js`.

### Against a previous release

Check out the release next to this repository and run the current benchmark scripts there:

```sh
git worktree add ../earcut-3.2.41 3.2.41
cp Test/bench/*.js Test/bench/*.fsx ../earcut-3.2.41/Test/bench/   # same benchmark scripts
cp .config/dotnet-tools.json ../earcut-3.2.41/.config/              # same Fable version
cd ../earcut-3.2.41
dotnet tool restore && dotnet fable && dotnet build -c Release
```

Then run the commands above in that folder.

## Files

- `bench-tiles.js`, `bench-tiles.fsx`: `earcut` over the MVT fixture.
- `bench-refine.js`, `bench-refine.fsx`: `refine` over the MVT fixture, starting from cached
  `earcut` output.
- `bench-large.js`, `bench-large.fsx`: large single rings from
  [mapbox/earcut#212](https://github.com/mapbox/earcut/pull/212), with concave and collinear
  control cases.
- `tiles-fixture.js`, `tiles-fixture.fsx`: readers for `tiles-fixture.bin`.
- `tiles-fixture.bin`: 119,680 polygons from real Mapbox Vector Tiles at zoom 4 to 16. Both test
  suites use it too.

The .NET scripts are ports of the JS ones and print the same figures, so their output can be
compared line by line.
