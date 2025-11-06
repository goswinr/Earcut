namespace Euclid


open System

/// Runtime.InteropServices.OptionalAttribute for member parameters.
type internal OPT =
    Runtime.InteropServices.OptionalAttribute

/// Runtime.InteropServices.DefaultParameterValueAttribute for member parameters.
type internal DEF =
    Runtime.InteropServices.DefaultParameterValueAttribute



type Earcut =

    // ///<summary> Triangulates a polygon with holes.</summary>
    // ///<param name="vertices">A ResizeArray of vertex coordinates like [x0, y0, x1, y1, x2, y2, ...].</param>
    // ///<param name="holeIndices">An optional ResizeArray of hole starting indices in the vertices ResizeArray.</param>
    // ///<param name="dim">The number of coordinates per vertex in the vertices array (2 by default).</param>
    // /// <returns>An ResizeArray of triangle vertex indices.</returns>
    // static member triangulate (vertices: ResizeArray<float>, [<OPT;DEF(null:ResizeArray<int>)>] holeIndices: ResizeArray<int>, [<OPT;DEF(2)>] dim: int) : ResizeArray<int> =
    //     Earcut.earcut (vertices, holeIndices, dim)


    class end