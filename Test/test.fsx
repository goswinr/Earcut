// F# Script to test Earcut
// Translation of test.js to F#

#r "nuget: Newtonsoft.Json, 13.0.4"

//#load "../Src/Earcut.fs"
#load "D:/Git/_Euclid_/Earcut/Src/Earcut.fs"

open System
open System.IO
open Newtonsoft.Json
open Earcut


// these tests pass standalone but fail when run in rhino:
//❌ FAIL: touching-holes3 rotation 90: deviation 0.247344 <= 0.000000
//❌ FAIL: touching-holes3 rotation 180: deviation 0.267071 <= 0.000000
//❌ FAIL: touching-holes4 rotation 270: deviation 0.084991 <= 0.000000
//❌ FAIL: touching-holes5 rotation 180: deviation 0.133536 <= 0.000000
//❌ FAIL: touching-holes5 rotation 270: deviation 0.257208 <= 0.000000
//❌ FAIL: water-huge rotation 180: deviation 0.041519 <= 0.003500
//❌ FAIL: water-huge2 rotation 180: deviation 0.085782 <= 0.061000
//❌ FAIL: water3 rotation 180: deviation 0.001152 <= 0.000000


// Helper to read JSON files
let readJson<'T> (filename: string) =
    let path = Path.Combine(__SOURCE_DIRECTORY__, filename)
    let json = File.ReadAllText(path)
    JsonConvert.DeserializeObject<'T>(json)

// Expected results
type ExpectedData = {
    triangles: Map<string, int>
    errors: Map<string, float>
    ``errors-with-rotation``: Map<string, float>
}

let expected = readJson<ExpectedData> "expected.json"

// Test assertion helper
let assertEqual (expected: 'T) (actual: 'T) (message: string) =
    if expected <> actual then
        printfn "❌ FAIL: %s" message
        printfn "   Expected: %A" expected
        printfn "   Actual:   %A" actual
        false
    else
        printfn "✅ PASS: %s" message
        true

let assertDeepEqual (expected: ResizeArray<int>) (actual: ResizeArray<int>) (message: string) =
    let exp = expected |> Seq.toArray
    let act = actual |> Seq.toArray
    assertEqual exp act message

let assertOk (condition: bool) (message: string) =
    if not condition then
        printfn "❌ FAIL: %s" message
        false
    else
        printfn "✅ PASS: %s" message
        true

// Track test results
let mutable passCount = 0
let mutable failCount = 0

let runTest (name: string) (testFn: unit -> bool) =
    try
        if testFn() then
            passCount <- passCount + 1
        else
            failCount <- failCount + 1
    with ex ->
        printfn "❌ FAIL: %s - Exception: %s" name ex.Message
        failCount <- failCount + 1

// Test: indices-2d
runTest "indices-2d" (fun () ->
    let indices = earcut([|10.0; 0.0; 0.0; 50.0; 60.0; 60.0; 70.0; 10.0|], null, 2)
    assertDeepEqual (ResizeArray([1; 0; 3; 1; 3; 2])) indices "indices-2d"
)

// Test: indices-3d
runTest "indices-3d" (fun () ->
    let indices = earcut([|10.0; 0.0; 0.0; 0.0; 50.0; 0.0; 60.0; 60.0; 0.0; 70.0; 10.0; 0.0|], null, 3)
    assertDeepEqual (ResizeArray([1; 0; 3; 1; 3; 2])) indices "indices-3d"
)

// Test: empty
runTest "empty" (fun () ->
    let indices = earcut([||], null, 2)
    assertDeepEqual (ResizeArray<int>()) indices "empty"
)

// Test fixtures with rotations
let fixtureIds = expected.triangles |> Map.toList |> List.map fst

// tracks the worst deviation across the three non-zero rotations per fixture,
// so we can tell when the errors-with-rotation bound can be tightened
let maxRotated = System.Collections.Generic.Dictionary<string, float>()

for id in fixtureIds do
    for rotation in [0; 90; 180; 270] do
        runTest (sprintf "%s rotation %d" id rotation) (fun () ->
            let coords = readJson<float[][][]> (sprintf "fixtures/%s.json" id)

            let theta = float rotation * Math.PI / 180.0
            let xx = Math.Round(Math.Cos(theta))
            let xy = Math.Round(-Math.Sin(theta))
            let yx = Math.Round(Math.Sin(theta))
            let yy = Math.Round(Math.Cos(theta))

            // Apply rotation if needed
            if rotation <> 0 then
                for ring in coords do
                    for coord in ring do
                        let x = coord.[0]
                        let y = coord.[1]
                        coord.[0] <- xx * x + xy * y
                        coord.[1] <- yx * x + yy * y

            let data = flatten coords
            let indices = earcut(data.vertices, data.holes, data.dimensions)
            let err = deviation(data.vertices, data.holes, data.dimensions, indices)

            let expectedTriangles = expected.triangles.[id]
            let expectedDeviation =
                if rotation <> 0 && expected.``errors-with-rotation``.ContainsKey(id) then
                    expected.``errors-with-rotation``.[id]
                elif expected.errors.ContainsKey(id) then
                    expected.errors.[id]
                else
                    0.0

            let numTriangles = indices.Count / 3

            let result1 =
                if rotation = 0 then
                    assertOk (numTriangles = expectedTriangles)
                        (sprintf "%s rotation %d: %d triangles when expected %d" id rotation numTriangles expectedTriangles)
                else
                    true

            let result2 =
                if expectedTriangles > 0 then
                    assertOk (err <= expectedDeviation)
                        (sprintf "%s rotation %d: deviation %f <= %f" id rotation err expectedDeviation)
                else
                    true

            // surface fixtures whose deviation is well below the recorded threshold (at least 3x),
            // so improvements after a correctness fix are visible and the threshold can be tightened;
            // for rotations, compare the worst of the three against the shared errors-with-rotation bound
            if rotation = 0 then
                if expectedDeviation > 0.0 && err * 3.0 < expectedDeviation then
                    printfn "ℹ %s: deviation %g < recorded %g (improved)" id err expectedDeviation
            else
                let prev = match maxRotated.TryGetValue id with | true, v -> v | _ -> 0.0
                maxRotated.[id] <- max prev err
                if rotation = 270 && expectedDeviation > 0.0 && maxRotated.[id] * 3.0 < expectedDeviation then
                    printfn "ℹ %s rotated: max deviation %g < recorded %g (improved)" id maxRotated.[id] expectedDeviation

            result1 && result2
        )

// Test: infinite-loop
runTest "infinite-loop" (fun () ->
    try
        let _ = earcut([|1.0; 2.0; 2.0; 2.0; 1.0; 2.0; 1.0; 1.0; 1.0; 2.0; 4.0; 1.0; 5.0; 1.0; 3.0; 2.0; 4.0; 2.0; 4.0; 1.0|], [|5|], 2)
        printfn "✅ PASS: infinite-loop (completed without hanging)"
        true
    with ex ->
        printfn "❌ FAIL: infinite-loop - Exception: %s" ex.Message
        false
)

// ============================================================
// Refine tests (port of the upstream refine unit tests)
// ============================================================

open System.Collections.Generic

// total edge length of all triangles (quality signal: refinement should reduce it)
let trianglePerimeter (triangles: ResizeArray<int>) (vertices: float[]) (dim: int) : float =
    let hyp (dx: float) (dy: float) = sqrt (dx * dx + dy * dy)
    let mutable perimeter = 0.0
    let mutable i = 0
    while i < triangles.Count do
        let ax = vertices.[triangles.[i] * dim]
        let ay = vertices.[triangles.[i] * dim + 1]
        let bx = vertices.[triangles.[i + 1] * dim]
        let by = vertices.[triangles.[i + 1] * dim + 1]
        let cx = vertices.[triangles.[i + 2] * dim]
        let cy = vertices.[triangles.[i + 2] * dim + 1]
        perimeter <- perimeter + hyp (ax - bx) (ay - by) + hyp (bx - cx) (by - cy) + hyp (cx - ax) (cy - ay)
        i <- i + 3
    perimeter

let nextHalfEdge (e: int) : int = e - e % 3 + (e + 1) % 3

let orientTest (ax, ay, bx, by, cx, cy) : float =
    (bx - ax) * (cy - ay) - (by - ay) * (cx - ax)

let inCircleTest (ax, ay, bx, by, cx, cy, px, py) : bool =
    let dx = ax - px
    let dy = ay - py
    let ex = bx - px
    let ey = by - py
    let fx = cx - px
    let fy = cy - py
    let ap = dx * dx + dy * dy
    let bp = ex * ex + ey * ey
    let cp = fx * fx + fy * fy
    dx * (ey * cp - bp * fy) - dy * (ex * cp - bp * fx) + ap * (ex * fy - ey * fx) <= 0.0

// count interior edges that are convex but not Delaunay (should be 0 after refine)
let countIllegalEdges (triangles: ResizeArray<int>) (vertices: float[]) (dim: int) : int =
    let n = triangles.Count
    let halfEdges = Array.create n -1
    let edges = Dictionary<struct (int * int), int>()

    for e = 0 to n - 1 do
        let a = triangles.[e]
        let b = triangles.[nextHalfEdge e]
        let key = if a < b then struct (a, b) else struct (b, a)
        match edges.TryGetValue key with
        | true, twin ->
            halfEdges.[e] <- twin
            halfEdges.[twin] <- e
            edges.Remove key |> ignore
        | _ ->
            edges.[key] <- e

    let mutable illegal = 0
    for a = 0 to n - 1 do
        let b = halfEdges.[a]
        if b <> -1 && a < b then
            let a0 = a - a % 3
            let b0 = b - b % 3
            let ar = a0 + (a + 2) % 3
            let al = a0 + (a + 1) % 3
            let bl = b0 + (b + 2) % 3
            let p0 = triangles.[ar]
            let pr = triangles.[a]
            let pl = triangles.[al]
            let p1 = triangles.[bl]

            let x0 = vertices.[p0 * dim]
            let y0 = vertices.[p0 * dim + 1]
            let xr = vertices.[pr * dim]
            let yr = vertices.[pr * dim + 1]
            let xl = vertices.[pl * dim]
            let yl = vertices.[pl * dim + 1]
            let x1 = vertices.[p1 * dim]
            let y1 = vertices.[p1 * dim + 1]
            let convex = orientTest (x0, y0, xr, yr, x1, y1) > 0.0 && orientTest (x0, y0, x1, y1, xl, yl) > 0.0

            if convex && not (inCircleTest (x0, y0, xr, yr, xl, yl, x1, y1)) then
                illegal <- illegal + 1
    illegal

runTest "refine improves a bad quad diagonal" (fun () ->
    let vertices = [| 0.0; 0.0;  3.0; 0.0;  10.0; 1.0;  0.0; 2.0 |]
    let triangles = ResizeArray([2; 3; 0;  2; 0; 1])
    let beforePerimeter = trianglePerimeter triangles vertices 2
    refine(triangles, vertices, 2)
    let afterPerimeter = trianglePerimeter triangles vertices 2

    assertDeepEqual (ResizeArray([2; 3; 1;  3; 0; 1])) triangles "refine improves a bad quad diagonal: indices" &&
    assertOk (afterPerimeter < beforePerimeter * 0.7) "refine improves a bad quad diagonal: perimeter reduced" &&
    assertOk (deviation(vertices, null, 2, triangles) = 0.0) "refine improves a bad quad diagonal: deviation 0"
)

runTest "refine leaves a good quad diagonal alone" (fun () ->
    let vertices = [| 0.0; 0.0;  5.0; 0.0;  4.0; 1.0;  0.0; 4.0 |]
    let triangles = ResizeArray([2; 3; 0;  2; 0; 1])

    refine(triangles, vertices, 2)

    assertDeepEqual (ResizeArray([2; 3; 0;  2; 0; 1])) triangles "refine leaves a good quad diagonal alone: indices" &&
    assertOk (deviation(vertices, null, 2, triangles) = 0.0) "refine leaves a good quad diagonal alone: deviation 0"
)

runTest "refine preserves a concave polygon" (fun () ->
    let vertices = [| 0.0; 0.0;  4.0; 0.0;  4.0; 1.0;  1.0; 1.0;  1.0; 4.0;  0.0; 4.0 |]
    let triangles = earcut(vertices, null, 2)
    let length = triangles.Count
    let beforePerimeter = trianglePerimeter triangles vertices 2
    refine(triangles, vertices, 2)
    let afterPerimeter = trianglePerimeter triangles vertices 2

    assertOk (triangles.Count = length) "refine preserves a concave polygon: triangle count" &&
    assertOk (afterPerimeter < beforePerimeter * 0.9) "refine preserves a concave polygon: perimeter reduced" &&
    assertOk (deviation(vertices, null, 2, triangles) = 0.0) "refine preserves a concave polygon: deviation 0"
)

runTest "refine terminates on near-cocircular points" (fun () ->
    // Four near-cocircular points make the non-robust inCircle test give inconsistent signs for an
    // edge and its flip; without the tie margin the Lawson cascade flips one edge back and forth
    // forever. Should return with a valid mesh of unchanged size and zero deviation.
    let vertices = [|
        127.65906365022843; 9.336137742499535; 124.21725103117963; 30.888097161477972
        91.35514946628345; 89.65621376119454; 40.10446780041529; 121.5550560957686
        -110.83205604043928; 64.03323632184248; -127.20394987965459; -14.253249980770189
        61.074962259031416; -112.48932831632469; 127.37846573978545; -12.598669206638515
        127.77010311801033; -7.668164657400608 |]
    let triangles = earcut(vertices, null, 2)
    let length = triangles.Count
    refine(triangles, vertices, 2)
    assertOk (triangles.Count = length) "refine terminates on near-cocircular points: triangle count" &&
    assertOk (deviation(vertices, null, 2, triangles) < 1e-15) "refine terminates on near-cocircular points: deviation"
)

runTest "refine legalizes all convex interior edges in earcut fixture" (fun () ->
    let coords = readJson<float[][][]> "fixtures/earcut.json"
    let data = flatten coords
    let triangles = earcut(data.vertices, data.holes, data.dimensions)

    refine(triangles, data.vertices, data.dimensions)

    assertOk (countIllegalEdges triangles data.vertices data.dimensions = 0)
        "refine legalizes all convex interior edges in earcut fixture: no illegal edges" &&
    assertOk (deviation(data.vertices, data.holes, data.dimensions, triangles) = 0.0)
        "refine legalizes all convex interior edges in earcut fixture: deviation 0"
)

// ============================================================
// MVT tiles fixture (port of Test/bench/tiles-fixture.js)
// ============================================================

// Reads tiles-fixture.bin: length-delimited packed-varint MVT geometry blobs,
// one per polygon feature. Decodes them with a small varint reader,
// reconstructs polygons by splitting multipolygons and classifying
// holes by signed area, per the MVT spec.
//
// tiles-fixture.bin format (little-endian LEB128 unsigned varints throughout):
//
//   file    := tile*                     (repeated until EOF)
//   tile    := zoom featureCount feature*
//   feature := geomLen geom              (geomLen = number of varints in geom)
//   geom    := uint32*                   (raw MVT command/parameter integers)
//
// The geom integers are the native MVT polygon encoding: MoveTo/LineTo/ClosePath
// command-integers interleaved with zigzag delta-encoded coordinate pairs.
type TilePoly = { vertices: float[]; holes: int[]; dimensions: int }

let readTilesFixture () : ResizeArray<TilePoly> =
    let buf = File.ReadAllBytes(Path.Combine(__SOURCE_DIRECTORY__, "bench", "tiles-fixture.bin"))
    let mutable pos = 0
    let polys = ResizeArray<TilePoly>()

    let readVarint () =
        let mutable value = 0
        let mutable shift = 0
        let mutable more = true
        while more do
            let b = buf.[pos]
            pos <- pos + 1
            value <- value ||| ((int (b &&& 0x7fuy)) <<< shift)
            shift <- shift + 7
            more <- b &&& 0x80uy <> 0uy
        value

    let zigZagDecode (n: int) = (n >>> 1) ^^^ (-(n &&& 1))

    let ringArea (ring: ResizeArray<float[]>) =
        let mutable sum = 0.0
        let mutable j = ring.Count - 1
        for i = 0 to ring.Count - 1 do
            sum <- sum + (ring.[j].[0] - ring.[i].[0]) * (ring.[i].[1] + ring.[j].[1])
            j <- i
        sum / 2.0

    let decodeMvtRings (geom: int[]) =
        let rings = ResizeArray<ResizeArray<float[]>>()
        let mutable x = 0
        let mutable y = 0
        let mutable ring : ResizeArray<float[]> = null
        let mutable i = 0
        while i < geom.Length do
            let cmd = geom.[i] &&& 0x7
            let count = geom.[i] >>> 3
            i <- i + 1
            if cmd = 1 then
                for _ = 1 to count do
                    x <- x + zigZagDecode geom.[i]
                    i <- i + 1
                    y <- y + zigZagDecode geom.[i]
                    i <- i + 1
                    if not (obj.ReferenceEquals(ring, null)) then rings.Add ring
                    ring <- ResizeArray([ [| float x; float y |] ])
            elif cmd = 2 then
                for _ = 1 to count do
                    x <- x + zigZagDecode geom.[i]
                    i <- i + 1
                    y <- y + zigZagDecode geom.[i]
                    i <- i + 1
                    ring.Add [| float x; float y |]
            elif cmd = 7 && not (obj.ReferenceEquals(ring, null)) then
                rings.Add ring
                ring <- null
        if not (obj.ReferenceEquals(ring, null)) then rings.Add ring
        rings

    let push (current: ResizeArray<ResizeArray<float[]>>) =
        let arr = current |> Seq.map (fun r -> r.ToArray()) |> Array.ofSeq
        let data = flatten arr
        polys.Add { vertices = data.vertices; holes = data.holes; dimensions = data.dimensions }

    while pos < buf.Length do
        readVarint () |> ignore // zoom (only used by the benchmarks)
        let features = readVarint ()
        for _ = 1 to features do
            let count = readVarint ()
            let geom = Array.zeroCreate count
            for i = 0 to count - 1 do
                geom.[i] <- readVarint ()

            let mutable current : ResizeArray<ResizeArray<float[]>> = null
            for ring in decodeMvtRings geom do
                if ring.Count >= 3 then
                    let area = ringArea ring
                    if area <> 0.0 then
                        if area > 0.0 then
                            if not (obj.ReferenceEquals(current, null)) then push current
                            current <- ResizeArray([ ring ])
                        elif not (obj.ReferenceEquals(current, null)) then
                            current.Add ring
            if not (obj.ReferenceEquals(current, null)) then push current

    polys

runTest "mvt fixture has zero deviation and refined quality" (fun () ->
    let polys = readTilesFixture ()
    let mutable nonzero = 0
    let mutable firstIndex = -1
    let mutable firstDev = 0.0
    let mutable worstIndex = -1
    let mutable worstDev = 0.0
    let mutable sumDev = 0.0
    let mutable refinedNonzero = 0
    let mutable refinedFirstIndex = -1
    let mutable refinedFirstDev = 0.0
    let mutable refinedWorstIndex = -1
    let mutable refinedWorstDev = 0.0
    let mutable refinedSumDev = 0.0
    let mutable lengthChanged = 0
    let mutable basePerimeter = 0.0
    let mutable refinedPerimeter = 0.0

    for i = 0 to polys.Count - 1 do
        let data = polys.[i]
        let triangles = earcut(data.vertices, data.holes, data.dimensions)
        let length = triangles.Count
        basePerimeter <- basePerimeter + trianglePerimeter triangles data.vertices data.dimensions
        let dev = deviation(data.vertices, data.holes, data.dimensions, triangles)
        if dev <> 0.0 then
            if firstIndex < 0 then
                firstIndex <- i
                firstDev <- dev
            nonzero <- nonzero + 1
            sumDev <- sumDev + dev
            if dev > worstDev then
                worstIndex <- i
                worstDev <- dev

        refine(triangles, data.vertices, data.dimensions)
        refinedPerimeter <- refinedPerimeter + trianglePerimeter triangles data.vertices data.dimensions
        if triangles.Count <> length then lengthChanged <- lengthChanged + 1

        let refinedDev = deviation(data.vertices, data.holes, data.dimensions, triangles)
        if refinedDev <> 0.0 then
            if refinedFirstIndex < 0 then
                refinedFirstIndex <- i
                refinedFirstDev <- refinedDev
            refinedNonzero <- refinedNonzero + 1
            refinedSumDev <- refinedSumDev + refinedDev
            if refinedDev > refinedWorstDev then
                refinedWorstIndex <- i
                refinedWorstDev <- refinedDev

    assertOk (polys.Count = 119680) (sprintf "mvt fixture: %d polygons when expected 119680" polys.Count) &&
    assertOk (nonzero = 0)
        (sprintf "mvt fixture: %d polygons with nonzero deviation; first %d: %g, worst %d: %g, sum %g"
            nonzero firstIndex firstDev worstIndex worstDev sumDev) &&
    assertOk (lengthChanged = 0) (sprintf "mvt fixture: %d refined triangulations changed triangle count" lengthChanged) &&
    assertOk (refinedNonzero = 0)
        (sprintf "mvt fixture: %d refined polygons with nonzero deviation; first %d: %g, worst %d: %g, sum %g"
            refinedNonzero refinedFirstIndex refinedFirstDev refinedWorstIndex refinedWorstDev refinedSumDev) &&
    assertOk (refinedPerimeter < basePerimeter * 0.72)
        (sprintf "mvt fixture: refined perimeter ratio %g < 0.72" (refinedPerimeter / basePerimeter))
)

// Regression for the hole-bridge block index (issue #183): a collinear-rich outer ring
// (integer grid, like MVT data) plus multiple holes used to drop a hole when filterPoints
// healed a collinear run across a block boundary, leaving the surviving edge outside its
// block's stale bbox so the leftward-ray scan false-skipped it. Assert full coverage.
runTest "block-index-collinear" (fun () ->
    let N = 30
    let outer = ResizeArray<float[]>()
    for x = 0 to N do outer.Add [| float x; 0.0 |]
    for y = 1 to N do outer.Add [| float N; float y |]
    for x = N - 1 downto 0 do outer.Add [| float x; float N |]
    for y = N - 1 downto 1 do outer.Add [| 0.0; float y |]
    let rect (x0: float) (y0: float) (w: float) (h: float) =
        [| [| x0; y0 |]; [| x0; y0 + h |]; [| x0 + w; y0 + h |]; [| x0 + w; y0 |] |]
    let rings = [| outer.ToArray(); rect 5.0 5.0 2.0 4.0; rect 2.0 23.0 1.0 1.0 |]

    let mutable ok = true
    for rotation in [0; 90; 180; 270] do
        let theta = float rotation * Math.PI / 180.0
        let xx = Math.Round(Math.Cos(theta))
        let xy = Math.Round(-Math.Sin(theta))
        let yx = Math.Round(Math.Sin(theta))
        let yy = Math.Round(Math.Cos(theta))
        let rotated = rings |> Array.map (Array.map (fun c -> [| xx * c.[0] + xy * c.[1]; yx * c.[0] + yy * c.[1] |]))
        let data = flatten rotated
        let indices = earcut(data.vertices, data.holes, data.dimensions)
        let err = deviation(data.vertices, data.holes, data.dimensions, indices)
        ok <- assertOk (err < 1e-9) (sprintf "block-index-collinear rotation %d: deviation %g (hole dropped?)" rotation err) && ok
    ok
)

// Print summary
printfn ""
printfn "=================="
printfn "Test Summary"
printfn "=================="
printfn "Passed: %d" passCount
printfn "Failed: %d" failCount
printfn "Total:  %d" (passCount + failCount)

if failCount = 0 then
    printfn $"🎉 All tests passed!"
    0
else
    printfn "⚠️  Some tests failed"
    1
