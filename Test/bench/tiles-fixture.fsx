// Reads tiles-fixture.bin for the .NET benchmarks, a port of tiles-fixture.js.
// Build first: dotnet build -c Release
module TilesFixture

#r "../../bin/Release/netstandard2.0/Earcut.dll"

open System.IO

// Reads tiles-fixture.bin: length-delimited packed-varint MVT geometry blobs,
// one per polygon feature. Decodes them with a small varint reader, reconstructs
// polygons by splitting multipolygons and classifying holes by signed area,
// per the MVT spec. See tiles-fixture.js for the file format.
type TilePoly = { vertices: float[]; holes: int[]; dimensions: int; z: int }

let readTilesFixture () : TilePoly[] =
    let buf = File.ReadAllBytes(Path.Combine(__SOURCE_DIRECTORY__, "tiles-fixture.bin"))
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

    while pos < buf.Length do
        let z = readVarint ()
        let features = readVarint ()

        let push (current: ResizeArray<ResizeArray<float[]>>) =
            let data = current |> Seq.map (fun r -> r.ToArray()) |> Array.ofSeq |> Earcut.flatten
            polys.Add { vertices = data.vertices; holes = data.holes; dimensions = data.dimensions; z = z }

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

    polys.ToArray()
