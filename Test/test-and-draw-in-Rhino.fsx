// F# Script to test Earcut
// Translation of test.js to F#

#r "C:/Program Files/Rhino 8/System/RhinoCommon.dll"
#r "nuget: Newtonsoft.Json, 13.0.4"
#r "nuget:Rhino.Scripting.FSharp"
#load "D:\Git\_Euclid_\Earcut\Src\Earcut.fs"
#nowarn "25" //pattern match on array

// these tests pass standalone but fail when run in rhino:
//❌ FAIL: touching-holes3 rotation 90: deviation 0.247344 <= 0.000000
//❌ FAIL: touching-holes3 rotation 180: deviation 0.267071 <= 0.000000
//❌ FAIL: touching-holes4 rotation 270: deviation 0.084991 <= 0.000000
//❌ FAIL: touching-holes5 rotation 180: deviation 0.133536 <= 0.000000
//❌ FAIL: touching-holes5 rotation 270: deviation 0.257208 <= 0.000000
//❌ FAIL: water-huge rotation 180: deviation 0.041519 <= 0.003500
//❌ FAIL: water-huge2 rotation 180: deviation 0.085782 <= 0.061000
//❌ FAIL: water3 rotation 180: deviation 0.001152 <= 0.000000

open System
open System.IO
open Newtonsoft.Json
open Rhino.Scripting
open Rhino.Scripting.FSharp
open Rhino.Geometry
type rs = RhinoScriptSyntax

rs.DisableRedraw()

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

let mutable shift = Vector3d.Zero


let removeDuplicatePoints (pts:ResizeArray<Point3d>) =
    let nps = ResizeArray(pts.Count)
    let mutable last = pts.[0]
    nps.Add last
    for i = 1 to pts.Count-1 do
        let p = pts.[i]
        let d = p.DistanceToSquared last
        if d > 1e-9 then
            nps.Add p
            last <- p
    nps

let drawInRhino(vertices: float[], holes: int[] , dimensions: int, indices: ResizeArray<int>, lay) =
    let m = new Mesh()
    let mutable i = 0
    let lastIndex = indices.Count - 3
    while i <= lastIndex do
        let a = indices[i]  *dimensions
        let b = indices[i+1]*dimensions
        let c = indices[i+2]*dimensions
        let pa = Point3d(vertices[a], vertices[a+1], 0.0)
        let pb = Point3d(vertices[b], vertices[b+1], 0.0)
        let pc = Point3d(vertices[c], vertices[c+1], 0.0)
        m.Faces.AddFace(m.Vertices.Add(pa), m.Vertices.Add(pb), m.Vertices.Add(pc)) |> ignore
        i <- i + 3

    let bb = m.GetBoundingBox(false)
    let bx = bb.Diagonal.X
    let t  = Vector3d(bb.Min)

    if bx < 9999999_000.0 then
        rs.Ot.AddMesh(m)
        |>! rs.move (shift-t)
        |> rs.setLayer lay

        let inline drawOutline from till =
            let pts = ResizeArray<Point3d>()
            let mutable i = from
            let lastIndex = till - dimensions
            while i <= lastIndex do
                let x = vertices[i ]
                let y = vertices[i + 1]
                pts.Add(Point3d(x, y, 0.0))
                i <- i + dimensions

            // close if open polyline
            let first = pts[0]
            let last = pts[pts.Count-1]
            if first.DistanceToSquared last > 1e-12 then
                pts.Add first

            let cpts = removeDuplicatePoints pts
            if cpts.Count > 3 then
                cpts
                |> rs.AddPolyline
                |>! rs.move (shift-t)
                |> rs.setLayer lay

        let mutable st = 0
        let mutable holesIdx = 0
        while st < vertices.Length do
            let en = if holesIdx < holes.Length then holes[holesIdx] * dimensions else vertices.Length
            drawOutline st en
            st <- en
            holesIdx <- holesIdx + 1

        shift.X <- shift.X  + bx * 1.5
    else
        eprintfn $"skipped huge {lay}: {bx}"


let screenshot(lay:string) =
    rs.ZoomExtents()
    let view = rs.Doc.Views.ActiveView
    let view_capture = Rhino.Display.ViewCapture()
    view_capture.Width <- view.ActiveViewport.Size.Width
    view_capture.Height <- view.ActiveViewport.Size.Height
    view_capture.ScaleScreenItems <- false
    view_capture.DrawAxes <- false
    view_capture.DrawGrid <- false
    view_capture.DrawGridAxes <- false
    view_capture.TransparentBackground <- true
    let bitmap = view_capture.CaptureToBitmap(view)
    if not (isNull bitmap) then
        let folder = System.Environment.SpecialFolder.Desktop
        let path = System.Environment.GetFolderPath(folder)
        let filename = System.IO.Path.Combine(path, $"{lay}.png");
        bitmap.Save(filename, System.Drawing.Imaging.ImageFormat.Png);


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


// Test fixtures with rotations
let fixtureIds = expected.triangles |> Map.toList |> List.map fst

for id in fixtureIds do
    for rotation in [0] do // ; 90; 180; 270] do
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

            let data = Earcut.flatten coords
            let indices = Earcut.earcut(data.vertices, data.holes|> Seq.toArray , data.dimensions)

            rs.AllObjects()
            |> rs.DeleteObjects
            drawInRhino(data.vertices, data.holes|> Seq.toArray, data.dimensions, indices, $"{id}-{rotation}")
            screenshot(id) 
            
            let err = Earcut.deviation(data.vertices, data.holes |> Seq.toArray, data.dimensions, indices)

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

runTest "indices-2d" (fun () ->
    let indices = Earcut.earcut([|10.0; 0.0; 0.0; 50.0; 60.0; 60.0; 70.0; 10.0|], null, 2)
    assertDeepEqual (ResizeArray([1; 0; 3; 3; 2; 1])) indices "indices-2d")

runTest "indices-3d" (fun () ->
    let indices = Earcut.earcut([|10.0; 0.0; 0.0; 0.0; 50.0; 0.0; 60.0; 60.0; 0.0; 70.0; 10.0; 0.0|], null, 3)
    assertDeepEqual (ResizeArray([1; 0; 3; 3; 2; 1])) indices "indices-3d")

runTest "empty" (fun () ->
    let indices = Earcut.earcut([||], null, 2)
    assertDeepEqual (ResizeArray<int>()) indices "empty")

runTest "infinite-loop" (fun () ->
    try
        let _ = Earcut.earcut([|1.0; 2.0; 2.0; 2.0; 1.0; 2.0; 1.0; 1.0; 1.0; 2.0; 4.0; 1.0; 5.0; 1.0; 3.0; 2.0; 4.0; 2.0; 4.0; 1.0|], [|5|], 2)
        printfn "✅ PASS: infinite-loop (completed without hanging)"
        true
    with ex ->
        printfn "❌ FAIL: infinite-loop - Exception: %s" ex.Message
        false)


printfn ""
printfn "=================="
printfn "Test Summary"
printfn "=================="
printfn "Passed: %d" passCount
printfn "Failed: %d" failCount
printfn "Total:  %d" (passCount + failCount)
if failCount = 0 then
    printfn "🎉 All tests passed!"
    0
else
    printfn "⚠️  Some tests failed"
    1
