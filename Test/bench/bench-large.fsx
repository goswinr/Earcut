// Build first: dotnet build -c Release
// Run in a fresh process: dotnet fsi Test/bench/bench-large.fsx
#r "../../bin/Release/netstandard2.0/Earcut.dll"

open System
open System.Diagnostics
open Earcut

let radial n radius =
    let vertices = Array.zeroCreate (2 * n)
    for i = 0 to n - 1 do
        let angle = 2.0 * Math.PI * float i / float n
        let r = radius i
        vertices.[2 * i] <- r * Math.Cos angle
        vertices.[2 * i + 1] <- r * Math.Sin angle
    vertices

// Collinear boundary vertices must keep using the normal ear checks.
let rectangle n =
    let vertices = Array.zeroCreate (2 * n)
    let corners = [| 0.0, 0.0; 2.0, 0.0; 2.0, 1.0; 0.0, 1.0 |]
    for side = 0 to 3 do
        let ax, ay = corners.[side]
        let bx, by = corners.[(side + 1) % 4]
        for i = 0 to n / 4 - 1 do
            let t = float i / float (n / 4)
            let index = 2 * (side * (n / 4) + i)
            vertices.[index] <- ax + (bx - ax) * t
            vertices.[index + 1] <- ay + (by - ay) * t
    vertices

let mutable seed = 212u
let randomRadius _ =
    seed <- seed * 1664525u + 1013904223u
    0.85 + 0.3 * float seed / 4294967296.0

let cases = [|
    "regular 10k-gon", radial 10000 (fun _ -> 1.0)
    "regular 100k-gon", radial 100000 (fun _ -> 1.0)
    "star 10k", radial 10000 (fun i -> if i % 2 = 1 then 0.5 else 1.0)
    "random-radius 10k", radial 10000 randomRadius
    "collinear rectangle 10k", rectangle 10000
|]

printfn ".NET %s; Release library; 1 warmup + 5 measured calls per case; median ms/call" (Environment.Version.ToString())
printfn "case                         median ms     min ms     max ms   triangles   checksum"
for name, vertices in cases do
    let triangles = earcut(vertices, null, 2) // warmup and untimed validation
    if not (deviation(vertices, null, 2, triangles) < 1e-9) then
        failwithf "%s: area deviation" name
    let mutable checksum = 0u
    for i = 0 to triangles.Count - 1 do
        checksum <- checksum + uint32 triangles.[i] * uint32 (i + 1)

    let times = Array.init 5 (fun _ ->
        let timer = Stopwatch.StartNew()
        let result = earcut(vertices, null, 2)
        timer.Stop()
        if result.Count <> triangles.Count then failwithf "%s: triangle count changed" name
        timer.Elapsed.TotalMilliseconds)
    Array.sortInPlace times
    printfn "%-28s %9.3f %10.3f %10.3f %11d %10u"
        name times.[2] times.[0] times.[4] (triangles.Count / 3) checksum
