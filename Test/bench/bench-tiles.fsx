// Benchmark earcut over a realistic set of MVT polygons, a port of bench-tiles.js.
// Build first: dotnet build -c Release
// Run in a fresh process: dotnet fsi Test/bench/bench-tiles.fsx
#load "tiles-fixture.fsx"

open System
open System.Diagnostics
open System.Globalization
open Earcut
open TilesFixture

let n0 (x: int) = x.ToString("N0", CultureInfo.InvariantCulture)

let polys = readTilesFixture () // each: {vertices, holes, dimensions, z} ready for earcut

// --- distribution (representativeness check) ---
let vc = polys |> Array.map (fun p -> p.vertices.Length / p.dimensions) |> Array.sort
let q f = vc.[min (vc.Length - 1) (int (floor (f * float vc.Length)))]
let withHoles = polys |> Array.filter (fun p -> p.holes.Length > 0) |> Array.length
let totalVerts = Array.sum vc

printfn ".NET %s; Release library" (Environment.Version.ToString())
printfn "fixture:  %s polygons, %s vertices" (n0 polys.Length) (n0 totalVerts)
printfn "          %.1f%% with holes" (100.0 * float withHoles / float polys.Length)
printfn "          verts/poly: median %d, p90 %d, p99 %d, max %d" (q 0.5) (q 0.9) (q 0.99) vc.[vc.Length - 1]

// --- timing ---
// lean timed path: just triangulate, as in bench-tiles.js.
let run (set: TilePoly[]) =
    let mutable tris = 0
    for d in set do tris <- tris + earcut(d.vertices, d.holes, d.dimensions).Count
    tris

// position-weighted fold over all indices, same value as in bench-tiles.js.
// Run once, never inside timing.
let checksum (set: TilePoly[]) =
    let mutable sum = 0u
    for d in set do
        let idx = earcut(d.vertices, d.holes, d.dimensions)
        for i = 0 to idx.Count - 1 do
            sum <- sum + uint32 idx.[i] * uint32 (i + 1)
    sum

// median wall time over 5 runs. Assumes the JIT is already warm, as in bench-tiles.js.
let timeSet (set: TilePoly[]) =
    let t = Array.init 5 (fun _ ->
        let sw = Stopwatch.StartNew()
        run set |> ignore
        sw.Elapsed.TotalMilliseconds)
    Array.sortInPlace t
    {| median = t.[2]; lo = t.[0]; hi = t.[4] |}

let tris = run polys
let sum = checksum polys
let overall = timeSet polys

printfn ""
printfn "result:   %s triangles (checksum %d)" (n0 (tris / 3)) sum
printfn "time:     %.0f ms   (%.0f-%.0f ms over 5 runs)" overall.median overall.lo overall.hi
printfn "speed:    %s polygons/s, %.1fM verts/s"
    (n0 (int (Math.Round(float polys.Length / (overall.median / 1000.0)))))
    (float totalVerts / (overall.median / 1000.0) / 1e6)

// --- per-zoom breakdown ---
printfn ""
printfn "per zoom:"
printfn "  z   polygons      verts   med.v   time     %%time   polys/s"
for z, set in polys |> Array.groupBy (fun d -> d.z) |> Array.sortBy fst do
    let v = set |> Array.map (fun d -> d.vertices.Length / d.dimensions) |> Array.sort
    let verts = Array.sum v
    let med = v.[v.Length / 2]
    let t = timeSet set // already warm from the overall pass
    let pct = 100.0 * t.median / overall.median
    printfn "  %2d  %8s  %9s  %5d  %5.0f ms  %5.1f%%  %8s"
        z (n0 set.Length) (n0 verts) med t.median pct
        (n0 (int (Math.Round(float set.Length / (t.median / 1000.0)))))
