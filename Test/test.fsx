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
    assertDeepEqual (ResizeArray([1; 0; 3; 3; 2; 1])) indices "indices-2d"
)

// Test: indices-3d
runTest "indices-3d" (fun () ->
    let indices = earcut([|10.0; 0.0; 0.0; 0.0; 50.0; 0.0; 60.0; 60.0; 0.0; 70.0; 10.0; 0.0|], null, 3)
    assertDeepEqual (ResizeArray([1; 0; 3; 3; 2; 1])) indices "indices-3d"
)

// Test: empty
runTest "empty" (fun () ->
    let indices = earcut([||], null, 2)
    assertDeepEqual (ResizeArray<int>()) indices "empty"
)

// Test fixtures with rotations
let fixtureIds = expected.triangles |> Map.toList |> List.map fst

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
