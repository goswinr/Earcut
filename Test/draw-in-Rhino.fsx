#r "C:/Program Files/Rhino 8/System/RhinoCommon.dll"
#r "nuget:Rhino.Scripting.FSharp"
#load "D:/Git/_Euclid_/Earcut/Src/Earcut.fs"

open Rhino.Scripting
open Rhino.Scripting.FSharp
open Rhino.Geometry
type rs = RhinoScriptSyntax

#nowarn "25" //pattern match on array

rs.DisableRedraw()


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

    rs.Ot.AddMesh(m) |> rs.setLayer lay

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
        
        pts
        |> rs.AddPolyline
        |> rs.setLayer lay

    let mutable st = 0
    let mutable holesIdx = 0
    while st < vertices.Length do
        let en = if holesIdx < holes.Length then holes[holesIdx] * dimensions else vertices.Length
        drawOutline st en
        st <- en
        holesIdx <- holesIdx + 1



//let vertices =  [|0.;0.; 100.;0.; 100.;100.; 0.;100.;  20.;20.; 80.;20.; 80.;80.; 20.;80.|]
let vertices =  [|0.;0.;-1; 100.;0.;-1; 100.;100.;-1; 0.;100.;-1;  20.;20.;-1; 80.;20.;-1; 80.;80.;-1; 20.;80.; -1|]
let dims = 3
let holes = [|4|]
let indices = Earcut.earcut(vertices,holes , dims)
drawInRhino(vertices, holes, dims, indices, "Test")