// Benchmark the optional Delaunay refinement post-pass over the MVT fixture,
// a port of bench-refine.js. earcut runs once per polygon at startup (untimed),
// each timed refine pass starts from a fresh copy of that output.
// Build first: dotnet build -c Release
// Run in a fresh process: dotnet fsi Test/bench/bench-refine.fsx
#load "tiles-fixture.fsx"

open System
open System.Diagnostics
open System.Globalization
open Earcut
open TilesFixture

let polys = readTilesFixture ()
let totalVerts = polys |> Array.sumBy (fun d -> d.vertices.Length / d.dimensions)

// earcut output cached once (untimed). `work` holds reusable copies refine mutates in place.
let baseTris = polys |> Array.map (fun d -> earcut(d.vertices, d.holes, d.dimensions))
let work = baseTris |> Array.map (fun t -> ResizeArray t)

// restore every work list to pristine earcut output (each timed refine must start from it)
let reset () =
    for i = 0 to baseTris.Length - 1 do
        let w = work.[i]
        let b = baseTris.[i]
        for j = 0 to b.Count - 1 do
            w.[j] <- b.[j]

let earcutAll () =
    let mutable s = 0
    for d in polys do s <- s + earcut(d.vertices, d.holes, d.dimensions).Count
    s

let refineAll () =
    let mutable s = 0
    for i = 0 to work.Length - 1 do
        refine(work.[i], polys.[i].vertices, polys.[i].dimensions)
        s <- s + work.[i].Count
    s

// median wall time over 5 runs, `prep` runs untimed before each run.
let timeSet (fn: unit -> int) (prep: unit -> unit) =
    let t = Array.init 5 (fun _ ->
        prep ()
        let sw = Stopwatch.StartNew()
        fn () |> ignore
        sw.Elapsed.TotalMilliseconds)
    Array.sortInPlace t
    {| median = t.[2]; lo = t.[0]; hi = t.[4] |}

// --- triangle quality (normalized q = 4*sqrt(3)*area / sum of squared edges, in [0,1]) ---
let quality (tris: ResizeArray<int>[]) =
    let mutable slivers = 0
    let mutable tinyAngle = 0
    let mutable perim = 0.0
    let mutable sumQ = 0.0
    let mutable count = 0
    for p = 0 to polys.Length - 1 do
        let c = polys.[p].vertices
        let dim = polys.[p].dimensions
        let idx = tris.[p]
        for i in 0 .. 3 .. idx.Count - 1 do
            let ax, ay = c.[idx.[i] * dim], c.[idx.[i] * dim + 1]
            let bx, by = c.[idx.[i + 1] * dim], c.[idx.[i + 1] * dim + 1]
            let cx, cy = c.[idx.[i + 2] * dim], c.[idx.[i + 2] * dim + 1]
            let area = abs ((bx - ax) * (cy - ay) - (by - ay) * (cx - ax)) / 2.0
            let e1 = (bx - ax) ** 2.0 + (by - ay) ** 2.0
            let e2 = (cx - bx) ** 2.0 + (cy - by) ** 2.0
            let e3 = (ax - cx) ** 2.0 + (ay - cy) ** 2.0
            let sumSq = e1 + e2 + e3
            let q = if sumSq > 0.0 then 4.0 * sqrt 3.0 * area / sumSq else 0.0
            if q < 0.1 then slivers <- slivers + 1
            // smallest angle via law of cosines
            let la, lb, lc = sqrt e1, sqrt e2, sqrt e3
            let orOne x = if x = 0.0 then 1.0 else x
            let angA = acos (min 1.0 (max -1.0 ((lb * lb + lc * lc - la * la) / orOne (2.0 * lb * lc))))
            let angB = acos (min 1.0 (max -1.0 ((la * la + lc * lc - lb * lb) / orOne (2.0 * la * lc))))
            let minAng = min angA (min angB (Math.PI - angA - angB)) * 180.0 / Math.PI
            if minAng < 1.0 then tinyAngle <- tinyAngle + 1
            perim <- perim + la + lb + lc
            sumQ <- sumQ + q
            count <- count + 1
    {| slivers = slivers; tinyAngle = tinyAngle; perim = perim; meanQ = sumQ / float count; tris = count |}

// warm both paths. Unlike bench-refine.js this takes several passes: after a single one,
// the tiered JIT is still recompiling refine and the first timed runs are up to 2x slower.
for _ in 1 .. 5 do
    earcutAll () |> ignore
    reset ()
    refineAll () |> ignore

let tEarcut = timeSet earcutAll ignore
let tRefine = timeSet refineAll reset
let overhead = tRefine.median / tEarcut.median * 100.0

let n0 (x: int) = x.ToString("N0", CultureInfo.InvariantCulture)
printfn ".NET %s; Release library" (Environment.Version.ToString())
printfn "fixture:  %s polygons, %s vertices" (n0 polys.Length) (n0 totalVerts)
printfn ""
printfn "earcut:          %.0f ms   (%.0f-%.0f ms over 5 runs)" tEarcut.median tEarcut.lo tEarcut.hi
printfn "refine:          %.0f ms   (+%.0f%% over earcut, %.0f-%.0f ms)" tRefine.median overhead tRefine.lo tRefine.hi

// `baseTris` is pristine earcut output; `work` was refined by the last timed pass.
let qa = quality baseTris
let qb = quality work
let pct (x: float) (y: float) = sprintf "%.0f%%" ((y - x) / x * 100.0)
let exp2 (x: float) = x.ToString("0.00e+0", CultureInfo.InvariantCulture)
printfn ""
printfn "quality:              earcut     refined    delta"
printfn "  slivers (q<0.1)   %8d  %8d    %s" qa.slivers qb.slivers (pct (float qa.slivers) (float qb.slivers))
printfn "  min-angle < 1deg  %8d  %8d    %s" qa.tinyAngle qb.tinyAngle (pct (float qa.tinyAngle) (float qb.tinyAngle))
printfn "  total perimeter   %8s  %8s    %s" (exp2 qa.perim) (exp2 qb.perim) (pct qa.perim qb.perim)
printfn "  mean q            %8.3f  %8.3f    +%s" qa.meanQ qb.meanQ (pct qa.meanQ qb.meanQ)
printfn "  triangles         %s (invariant: %b)" (n0 qa.tris) (qa.tris = qb.tris)
