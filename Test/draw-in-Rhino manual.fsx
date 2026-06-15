#r "C:/Program Files/Rhino 8/System/RhinoCommon.dll"
#r "nuget: Rhino.Scripting.FSharp"
#r "nuget: Euclid.Rhino, 0.40.0"
#load "D:/Git/_Euclid_/Earcut/Src/Earcut.fs"
// #r "nuget: Earcut"
open Rhino.Scripting
open Rhino.Scripting.FSharp
open Rhino.Geometry
type rs = RhinoScriptSyntax
open Euclid


let poly =
    Polyline2D.create [|
        Pt(22.298834996945466, -36.232723389993794); Pt(33.41590916269559, -32.5099167275781); Pt(36.912887526702235, -42.9526133042276);
        Pt(25.795813570361688, -46.67542015260183); Pt(24.64044525268723, -43.22525565774889); Pt(27.204853057861328, -42.36650466918945);
        Pt(25.71890640258789, -37.929161071777344); Pt(23.154498596388343, -38.78791206068018); Pt(22.298834996945466, -36.232723389993794)
        |]

let hole = Polyline2D.create [|
    Pt(28.26544530179709, -54.05025514340622); Pt(43.829349517822266, -48.83832550048828); Pt(41.35971737895544, -41.46349083131277);
    Pt(36.579349517822266, -27.188322067260742); Pt(32.132518721770595, -28.677445839017636); Pt(21.015443801879883, -32.40025329589844);
    Pt(13.699999809265137, -34.849998474121094); Pt(20.950000762939453, -56.5); Pt(28.26544530179709, -54.05025514340622)
    |]



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


let vertices =  [|
    for pt in poly.AsPoints do
        pt.X
        pt.Y
    for pt in hole.AsPoints do
        pt.X
        pt.Y
    |]
let holes = [|poly.AsPoints.Count|]
let indices = Earcut.earcut(vertices,holes , 2)
drawInRhino(vertices, holes, 2, indices, "Test")