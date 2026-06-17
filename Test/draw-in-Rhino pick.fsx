#r "D:/Git/_Euclid_/Earcut/bin/Debug/net6.0/Earcut.dll"
#r "C:/Program Files/Rhino 8/System/RhinoCommon.dll"
#r "nuget: Rhino.Scripting.FSharp"
#r "nuget: Euclid.Rhino, 0.40.0"
// #load "D:/Git/_Euclid_/Earcut/Src/Earcut.fs"

open Rhino.Scripting
open Rhino.Scripting.FSharp
open Rhino.Geometry
type rs = RhinoScriptSyntax
open Euclid

rs.DisableRedraw()

do 
    let holes =
        rs.GetObjects("Select holes")
        |> Seq.map rs.CoercePolyline
        |> Seq.map Polyline2D.ofRhPolyline
        |> Seq.map _.XYs
        |> Array.ofSeq
    
    
    let outer =
        rs.GetObject("Select outer polyline")
        |> rs.CoercePolyline
        |> Polyline2D.ofRhPolyline
    
    
    let trias = Earcut.earcutTriangles holes outer.XYs
    
    
    let mutable i = 0
    while i < trias.Length do
        let x1 = trias.[i]
        let y1 = trias.[i + 1]
        let x2 = trias.[i + 2]
        let y2 = trias.[i + 3]
        let x3 = trias.[i + 4]
        let y3 = trias.[i + 5]
        let p1 = Point3d(x1, y1, 0.0)
        let p2 = Point3d(x2, y2, 0.0)
        let p3 = Point3d(x3, y3, 0.0)
        let pl = Polyline([| p1; p2; p3; p1 |])
        rs.AddPolyline(pl) |> ignore
    
        i <- i + 6