// a port of earcut.js

// https://github.com/mapbox/earcut

// https://github.com/mapbox/earcut/blob/54cbb0d38ece2441e510e61a40d187c8f6f514da/src/earcut.js

// v3.0.2 from 2025-11-6
module Earcut


type internal Node = {
    /// vertex index in coordinates array
    i: int
    /// vertex x coordinates
    x: float
    /// vertex y coordinates
    y: float
    /// previous vertex nodes in a polygon ring
    mutable prev: Node
    /// next vertex nodes in a polygon ring
    mutable next: Node
    /// z-order curve value
    mutable z: int
    /// previous nodes in z-order
    mutable prevZ: Node
    /// next nodes in z-order
    mutable nextZ: Node
    /// indicates whether this is a Steiner point
    mutable steiner: bool
}

let inline internal createNode(i: int, x: float, y: float) : Node =
    {
        i = i
        x = x
        y = y
        prev = Unchecked.defaultof<Node>
        next = Unchecked.defaultof<Node>
        z = 0
        prevZ = Unchecked.defaultof<Node>
        nextZ = Unchecked.defaultof<Node>
        steiner = false
    }

let inline internal (===) (a:Node) (b:Node) = obj.ReferenceEquals(a, b)

let inline internal (!==) (a:Node) (b:Node) = not <| obj.ReferenceEquals(a, b)

let inline internal notNull (node: Node) : bool = not <| obj.ReferenceEquals(node, Unchecked.defaultof<Node>)

let inline internal isNull (node: Node) : bool = obj.ReferenceEquals(node, Unchecked.defaultof<Node>)

// create a node and optionally link it with previous one (in a circular doubly linked list)
let inline internal insertNode (i: int, x: float, y: float, last: Node) : Node =
    let p = createNode(i, x, y)
    if isNull last then
        p.prev <- p
        p.next <- p
    else
        p.next <- last.next
        p.prev <- last
        last.next.prev <- p
        last.next <- p
    p

let inline internal removeNode (p: Node) : unit =
    p.next.prev <- p.prev
    p.prev.next <- p.next

    if notNull p.prevZ then p.prevZ.nextZ <- p.nextZ
    if notNull p.nextZ then p.nextZ.prevZ <- p.prevZ

// signed area of a triangle
let inline internal area (p: Node, q: Node, r: Node) : float =
    (q.y - p.y) * (r.x - q.x) - (q.x - p.x) * (r.y - q.y)

// check if two points are equal
let inline internal equals (p1: Node, p2: Node) : bool =
    p1.x = p2.x && p1.y = p2.y

// check if a point lies within a convex triangle
let inline internal pointInTriangle(ax: float, ay: float, bx: float, by: float, cx: float, cy: float, px: float, py: float) : bool =
    (cx - px) * (ay - py) >= (ax - px) * (cy - py) &&
    (ax - px) * (by - py) >= (bx - px) * (ay - py) &&
    (bx - px) * (cy - py) >= (cx - px) * (by - py)

// check if a point lies within a convex triangle but false if its equal to the first point of the triangle
let inline internal pointInTriangleExceptFirst(ax: float, ay: float, bx: float, by: float, cx: float, cy: float, px: float, py: float) : bool =
    not (ax = px && ay = py) && pointInTriangle(ax, ay, bx, by, cx, cy, px, py)

// for collinear points p, q, r, check if point q lies on segment pr
let inline internal onSegment(p: Node, q: Node, r: Node) : bool =
    q.x <= max p.x r.x && q.x >= min p.x r.x && q.y <= max p.y r.y && q.y >= min p.y r.y

let inline internal sign(num: float) : int =
    if num > 0.0 then 1
    elif num < 0.0 then -1
    else 0

// check if two segments intersect
let inline internal intersects(p1: Node, q1: Node, p2: Node, q2: Node) : bool =
    let o1 = sign(area(p1, q1, p2))
    let o2 = sign(area(p1, q1, q2))
    let o3 = sign(area(p2, q2, p1))
    let o4 = sign(area(p2, q2, q1))

    if o1 <> o2 && o3 <> o4 then true // general case
    elif o1 = 0 && onSegment(p1, p2, q1) then true // p1, q1 and p2 are collinear and p2 lies on p1q1
    elif o2 = 0 && onSegment(p1, q2, q1) then true // p1, q1 and q2 are collinear and q2 lies on p1q1
    elif o3 = 0 && onSegment(p2, p1, q2) then true // p2, q2 and p1 are collinear and p1 lies on p2q2
    elif o4 = 0 && onSegment(p2, q1, q2) then true // p2, q2 and q1 are collinear and q1 lies on p2q2
    else false

// check if a polygon diagonal intersects any polygon segments
let inline internal intersectsPolygon(a: Node, b: Node) : bool =
    let mutable p = a
    let mutable result = false
    let mutable continueLoop = true
    // do-while loop: execute once then check condition
    while continueLoop do
        if p.i <> a.i && p.next.i <> a.i && p.i <> b.i && p.next.i <> b.i &&
            intersects(p, p.next, a, b) then
            result <- true
            continueLoop <- false
        else
            p <- p.next
            if p === a then continueLoop <- false
    result

// check if a polygon diagonal is locally inside the polygon
let inline internal locallyInside(a: Node, b: Node) : bool =
    if area(a.prev, a, a.next) < 0.0 then
        area(a, b, a.next) >= 0.0 && area(a, a.prev, b) >= 0.0
    else
        area(a, b, a.prev) < 0.0 || area(a, a.next, b) < 0.0

// check if the middle point of a polygon diagonal is inside the polygon
let inline internal middleInside(a: Node, b: Node) : bool =
    let mutable p = a
    let mutable inside = false
    let px = (a.x + b.x) / 2.0
    let py = (a.y + b.y) / 2.0
    let mutable continueLoop = true
    // do-while loop: execute once then check condition
    while continueLoop do
        if ((p.y > py) <> (p.next.y > py)) && p.next.y <> p.y &&
            (px < (p.next.x - p.x) * (py - p.y) / (p.next.y - p.y) + p.x) then
            inside <- not inside
        p <- p.next
        if p === a then continueLoop <- false
    inside

// link two polygon vertices with a bridge; if the vertices belong to the same ring, it splits polygon into two;
// if one belongs to the outer ring and another to a hole, it merges it into a single ring
let inline internal splitPolygon(a: Node, b: Node) : Node =
    let a2 = createNode(a.i, a.x, a.y)
    let b2 = createNode(b.i, b.x, b.y)
    let an = a.next
    let bp = b.prev

    a.next <- b
    b.prev <- a

    a2.next <- an
    an.prev <- a2

    b2.next <- a2
    a2.prev <- b2

    bp.next <- b2
    b2.prev <- bp

    b2

// check if a diagonal between two polygon nodes is valid (lies in polygon interior)
let inline internal isValidDiagonal(a: Node, b: Node) : bool =
    a.next.i <> b.i && a.prev.i <> b.i && not (intersectsPolygon(a, b)) && // doesn't intersect other edges
    (locallyInside(a, b) && locallyInside(b, a) && middleInside(a, b) && // locally visible
     (area(a.prev, a, b.prev) <> 0.0 || area(a, b.prev, b) <> 0.0) || // does not create opposite-facing sectors
     equals(a, b) && area(a.prev, a, a.next) > 0.0 && area(b.prev, b, b.next) > 0.0) // special zero-length case

// z-order of a point given coords and inverse of the longer side of data bbox
let inline internal zOrder(x: float, y: float, minX: float, minY: float, invSize: float) : int =
    // coords are transformed into non-negative 15-bit integer range
    let mutable x = int ((x - minX) * invSize)
    let mutable y = int ((y - minY) * invSize)

    x <- (x ||| (x <<< 8)) &&& 0x00FF00FF
    x <- (x ||| (x <<< 4)) &&& 0x0F0F0F0F
    x <- (x ||| (x <<< 2)) &&& 0x33333333
    x <- (x ||| (x <<< 1)) &&& 0x55555555

    y <- (y ||| (y <<< 8)) &&& 0x00FF00FF
    y <- (y ||| (y <<< 4)) &&& 0x0F0F0F0F
    y <- (y ||| (y <<< 2)) &&& 0x33333333
    y <- (y ||| (y <<< 1)) &&& 0x55555555

    x ||| (y <<< 1)

// find the leftmost node of a polygon ring
let inline internal getLeftmost(start: Node) : Node =
    let mutable p = start
    let mutable leftmost = start
    let mutable continueLoop = true
    // do-while loop: execute once then check condition
    while continueLoop do
        if p.x < leftmost.x || (p.x = leftmost.x && p.y < leftmost.y) then
            leftmost <- p
        p <- p.next
        if p === start then continueLoop <- false
    leftmost

// whether sector in vertex m contains sector in vertex p in the same coordinates
let inline internal sectorContainsSector(m: Node, p: Node) : bool =
    area(m.prev, m, p.prev) < 0.0 && area(p.next, m, m.next) < 0.0

// Simon Tatham's linked list merge sort algorithm
// http://www.chiark.greenend.org.uk/~sgtatham/algorithms/listsort.html
let rec internal sortLinked(list: Node) : Node =
    let mutable numMerges = 0
    let mutable inSize = 1
    let mutable list = list
    let mutable continueOuter = true

    while continueOuter do
        let mutable p = list
        list <- Unchecked.defaultof<Node>
        let mutable tail = Unchecked.defaultof<Node>
        numMerges <- 0

        while notNull p do
            numMerges <- numMerges + 1
            let mutable q = p
            let mutable pSize = 0
            let mutable i = 0
            while i < inSize do
                pSize <- pSize + 1
                q <- q.nextZ
                if isNull q then i <- inSize // exit loop
                else i <- i + 1

            let mutable qSize = inSize

            while pSize > 0 || (qSize > 0 && notNull q) do
                let mutable e = Unchecked.defaultof<Node>

                if pSize <> 0 && (qSize = 0 || isNull q || p.z <= q.z) then
                    e <- p
                    p <- p.nextZ
                    pSize <- pSize - 1
                else
                    e <- q
                    q <- q.nextZ
                    qSize <- qSize - 1

                if notNull tail then tail.nextZ <- e
                else list <- e

                e.prevZ <- tail
                tail <- e

            p <- q

        tail.nextZ <- Unchecked.defaultof<Node>
        inSize <- inSize * 2

        if numMerges <= 1 then continueOuter <- false

    list

// interlink polygon nodes in z-order
let internal indexCurve(start: Node, minX: float, minY: float, invSize: float) : unit =
    let mutable p = start
    let mutable continueLoop = true
    // do-while loop: execute once then check condition
    while continueLoop do
        if p.z = 0 then p.z <- zOrder(p.x, p.y, minX, minY, invSize)
        p.prevZ <- p.prev
        p.nextZ <- p.next
        p <- p.next
        if p === start then continueLoop <- false

    p.prevZ.nextZ <- Unchecked.defaultof<Node>
    p.prevZ <- Unchecked.defaultof<Node>

    sortLinked(p) |> ignore

let inline internal signedArea(data: array<float>, start: int, end_: int, dim: int) : float =
    let mutable sum = 0.0
    let mutable i = start
    let mutable j = end_ - dim
    while i < end_ do
        sum <- sum + (data.[j] - data.[i]) * (data.[i + 1] + data.[j + 1])
        j <- i
        i <- i + dim
    sum

// create a circular doubly linked list from polygon points in the specified winding order
let internal linkedList(data: array<float>, start: int, end_: int, dim: int, clockwise: bool) : Node =
    let mutable last = Unchecked.defaultof<Node>

    if clockwise = (signedArea(data, start, end_, dim) > 0.0) then
        let mutable i = start
        while i < end_ do
            last <- insertNode(i / dim, data.[i], data.[i + 1], last)
            i <- i + dim
    else
        let mutable i = end_ - dim
        while i >= start do
            last <- insertNode(i / dim, data.[i], data.[i + 1], last)
            i <- i - dim

    if notNull last && equals(last, last.next) then
        removeNode(last)
        last <- last.next

    last

// eliminate colinear or duplicate points
// in JS this sometimes gets called with just one argument
let rec internal filterPoints(start: Node, ende: Node) : Node =
    if isNull start then
        start
    else
        let mutable ende = if isNull ende then start else ende
        let mutable p = start
        let mutable continueLoop = true

        // do-while loop: execute once then check condition
        while continueLoop do

            if not p.steiner && (equals(p, p.next) || area(p.prev, p, p.next) = 0.0) then
                removeNode(p)
                ende <- p.prev
                p <- ende
                if p === p.next then
                    continueLoop <-  p !== ende
                else
                    continueLoop <- true
            else
                p <- p.next
                continueLoop <-  p !== ende

        ende

// check whether a polygon node forms a valid ear with adjacent nodes
let internal isEar(ear: Node) : bool =
    let a = ear.prev
    let b = ear
    let c = ear.next

    if area(a, b, c) >= 0.0 then false // reflex, can't be an ear
    else
        // now make sure we don't have other points inside the potential ear
        let ax = a.x
        let bx = b.x
        let cx = c.x
        let ay = a.y
        let by = b.y
        let cy = c.y

        // triangle bbox
        let x0 = min (min ax bx) cx
        let y0 = min (min ay by) cy
        let x1 = max (max ax bx) cx
        let y1 = max (max ay by) cy

        let mutable p = c.next
        let mutable result = true
        while p !== a && result do
            if p.x >= x0 && p.x <= x1 && p.y >= y0 && p.y <= y1 &&
                pointInTriangleExceptFirst(ax, ay, bx, by, cx, cy, p.x, p.y) &&
                area(p.prev, p, p.next) >= 0.0 then
                result <- false
            p <- p.next
        result

let internal isEarHashed(ear: Node, minX: float, minY: float, invSize: float) : bool =
    let a = ear.prev
    let b = ear
    let c = ear.next

    if area(a, b, c) >= 0.0 then false // reflex, can't be an ear
    else
        let ax = a.x
        let bx = b.x
        let cx = c.x
        let ay = a.y
        let by = b.y
        let cy = c.y

        // triangle bbox
        let x0 = min (min ax bx) cx
        let y0 = min (min ay by) cy
        let x1 = max (max ax bx) cx
        let y1 = max (max ay by) cy

        // z-order range for the current triangle bbox;
        let minZ = zOrder(x0, y0, minX, minY, invSize)
        let maxZ = zOrder(x1, y1, minX, minY, invSize)

        let mutable p = ear.prevZ
        let mutable n = ear.nextZ
        let mutable result = true

        // look for points inside the triangle in both directions
        while result && notNull p && p.z >= minZ && notNull n && n.z <= maxZ do
            if p.x >= x0 && p.x <= x1 && p.y >= y0 && p.y <= y1 && p !== a && p !== c &&
                pointInTriangleExceptFirst(ax, ay, bx, by, cx, cy, p.x, p.y) && area(p.prev, p, p.next) >= 0.0 then
                result <- false
            else
                p <- p.prevZ

            if result && notNull n && n.x >= x0 && n.x <= x1 && n.y >= y0 && n.y <= y1 && n !== a && n !== c &&
                pointInTriangleExceptFirst(ax, ay, bx, by, cx, cy, n.x, n.y) && area(n.prev, n, n.next) >= 0.0 then
                result <- false
            else if result then
                n <- n.nextZ

        // look for remaining points in decreasing z-order
        while result && notNull p && p.z >= minZ do
            if p.x >= x0 && p.x <= x1 && p.y >= y0 && p.y <= y1 && p !== a && p !== c &&
                pointInTriangleExceptFirst(ax, ay, bx, by, cx, cy, p.x, p.y) && area(p.prev, p, p.next) >= 0.0 then
                result <- false
            else
                p <- p.prevZ

        // look for remaining points in increasing z-order
        while result && notNull n && n.z <= maxZ do
            if n.x >= x0 && n.x <= x1 && n.y >= y0 && n.y <= y1 && n !== a && n !== c &&
                pointInTriangleExceptFirst(ax, ay, bx, by, cx, cy, n.x, n.y) && area(n.prev, n, n.next) >= 0.0 then
                result <- false
            else
                n <- n.nextZ

        result

// go through all polygon nodes and cure small local self-intersections
let internal cureLocalIntersections(start: Node, triangles: ResizeArray<int>) : Node =
    let mutable p = start
    let mutable start = start
    let mutable continueLoop = true
    // do-while loop: execute once then check condition
    while continueLoop do
        let a = p.prev
        let b = p.next.next

        if not (equals(a, b)) && intersects(a, p, p.next, b) && locallyInside(a, b) && locallyInside(b, a) then
            triangles.Add(a.i)
            triangles.Add(p.i)
            triangles.Add(b.i)

            // remove two nodes involved
            removeNode(p)
            removeNode(p.next)

            start <- b
            p <- start

        p <- p.next
        continueLoop <- p !== start

    filterPoints(p, Unchecked.defaultof<Node>)

// try splitting polygon into two and triangulate them independently
let rec internal splitEarcut(start: Node, triangles: ResizeArray<int>, dim: int, minX: float, minY: float, invSize: float) : unit =
    // look for a valid diagonal that divides the polygon into two
    let mutable a = start
    let mutable outerContinue = true
    while outerContinue do
        let mutable b = a.next.next
        let mutable innerContinue = true
        while innerContinue do
            if a.i <> b.i && isValidDiagonal(a, b) then
                // split the polygon in two by the diagonal
                let mutable c = splitPolygon(a, b)

                // filter colinear points around the cuts
                a <- filterPoints(a, a.next)
                c <- filterPoints(c, c.next)

                // run earcut on each half
                earcutLinked(a, triangles, dim, minX, minY, invSize, 0)
                earcutLinked(c, triangles, dim, minX, minY, invSize, 0)
                outerContinue <- false
                innerContinue <- false
            else
                b <- b.next
                innerContinue <-  b !== a.prev

        if outerContinue then
            a <- a.next
            outerContinue <- a !== start

// main ear slicing loop which triangulates a polygon (given as a linked list)
and internal earcutLinked(ear: Node, triangles: ResizeArray<int>, dim: int, minX: float, minY: float, invSize: float, pass: int) : unit =
    if isNull ear then ()
    else
        // interlink polygon nodes in z-order
        if pass = 0 && invSize <> 0.0 then indexCurve(ear, minX, minY, invSize)

        let mutable ear = ear
        let mutable stop = ear
        let mutable continueLoop = true

        // iterate through ears, slicing them one by one
        while continueLoop && ear.prev !== ear.next do
            let prev = ear.prev
            let next = ear.next

            let isEarResult =
                if invSize <> 0.0 then isEarHashed(ear, minX, minY, invSize)
                else isEar(ear)

            if isEarResult then
                triangles.Add(prev.i) // cut off the triangle
                triangles.Add(ear.i)
                triangles.Add(next.i)

                removeNode(ear)

                // skipping the next vertex leads to less sliver triangles
                ear <- next.next
                stop <- next.next
            else
                ear <- next

                // if we looped through the whole remaining polygon and can't find any more ears
                if ear === stop then
                    // try filtering points and slicing again
                    if pass = 0 then
                        earcutLinked(filterPoints(ear, Unchecked.defaultof<Node>), triangles, dim, minX, minY, invSize, 1)
                    // if this didn't work, try curing all small self-intersections locally
                    elif pass = 1 then
                        ear <- cureLocalIntersections(filterPoints(ear, Unchecked.defaultof<Node>), triangles)
                        earcutLinked(ear, triangles, dim, minX, minY, invSize, 2)
                    // as a last resort, try splitting the remaining polygon into two
                    elif pass = 2 then
                        splitEarcut(ear, triangles, dim, minX, minY, invSize)

                    continueLoop <- false

let internal compareXYSlope(a: Node) (b: Node) : int =
    let mutable result = a.x - b.x
    // when the left-most point of 2 holes meet at a vertex, sort the holes counterclockwise so that when we find
    // the bridge to the outer shell is always the point that they meet at.
    if result = 0.0 then
        result <- a.y - b.y
        if result = 0.0 then
            let aSlope = (a.next.y - a.y) / (a.next.x - a.x)
            let bSlope = (b.next.y - b.y) / (b.next.x - b.x)
            result <- aSlope - bSlope

    if result = 0.0 then 0
    else if result > 0.0 then 1
    else -1

// David Eberly's algorithm for finding a bridge between hole and outer polygon
let internal findHoleBridge(hole: Node, outerNode: Node) : Node =
    let mutable p = outerNode
    let hx = hole.x
    let hy = hole.y
    let mutable qx = -infinity
    let mutable m = Unchecked.defaultof<Node>

    // find a segment intersected by a ray from the hole's leftmost point to the left;
    // segment's endpoint with lesser x will be potential connection point
    // unless they intersect at a vertex, then choose the vertex
    if equals(hole, p) then p
    else
        let mutable continueLoop = true
        while continueLoop do
            if equals(hole, p.next) then
                m <- p.next
                continueLoop <- false
            elif hy <= p.y && hy >= p.next.y && p.next.y <> p.y then
                let x = p.x + (hy - p.y) * (p.next.x - p.x) / (p.next.y - p.y)
                if x <= hx && x > qx then
                    qx <- x
                    m <- if p.x < p.next.x then p else p.next
                    if x = hx then
                        continueLoop <- false // hole touches outer segment; pick leftmost endpoint

                if continueLoop then
                    p <- p.next
                    if p === outerNode then continueLoop <- false
            else
                p <- p.next
                if p === outerNode then continueLoop <- false

        if isNull m then Unchecked.defaultof<Node>
        else
            // look for points inside the triangle of hole point, segment intersection and endpoint;
            // if there are no points found, we have a valid connection;
            // otherwise choose the point of the minimum angle with the ray as connection point

            let stop = m
            let mx = m.x
            let my = m.y
            let mutable tanMin = infinity

            p <- m

            let mutable continueLoop = true
            // do-while loop: execute once then check condition
            while continueLoop do
                if hx >= p.x && p.x >= mx && hx <> p.x &&
                    pointInTriangle((if hy < my then hx else qx), hy, mx, my, (if hy < my then qx else hx), hy, p.x, p.y) then

                    let tan = abs(hy - p.y) / (hx - p.x) // tangential

                    if locallyInside(p, hole) &&
                        (tan < tanMin || (tan = tanMin && (p.x > m.x || (p.x = m.x && sectorContainsSector(m, p))))) then
                        m <- p
                        tanMin <- tan

                p <- p.next
                if p === stop then continueLoop <- false

            m

// find a bridge between vertices that connects hole with an outer ring and link it
let internal eliminateHole(hole: Node, outerNode: Node) : Node =
    let bridge = findHoleBridge(hole, outerNode)
    if isNull bridge then
        outerNode
    else
        let bridgeReverse = splitPolygon(bridge, hole)

        // filter collinear points around the cuts
        filterPoints(bridgeReverse, bridgeReverse.next) |> ignore
        filterPoints(bridge, bridge.next)

// link every hole into the outer loop, producing a single-ring polygon without holes
let internal eliminateHoles(data: array<float>, holeIndices: array<int>, outerNode: Node, dim: int) : Node =
    let queue = ResizeArray<Node>()

    for i = 0 to holeIndices.Length - 1 do
        let start = holeIndices.[i] * dim
        let end_ = if i < holeIndices.Length - 1 then holeIndices.[i + 1] * dim else data.Length
        let list = linkedList(data, start, end_, dim, false)
        if list === list.next then list.steiner <- true
        queue.Add(getLeftmost(list))


    queue.Sort compareXYSlope

    // process holes from left to right
    let mutable outerNode = outerNode
    for i = 0 to queue.Count - 1 do
        outerNode <- eliminateHole(queue.[i], outerNode)

    outerNode


///<summary> Triangulates a polygon with holes.</summary>
///<param name="vertices">A array of vertex coordinates like [x0, y0, x1, y1, x2, y2, ...].</param>
///<param name="holeIndices">An array of hole starting indices in the vertices array. Use `null` if there are no holes.</param>
///<param name="dimensions">The number of coordinates per vertex in the vertices array:
/// 2 if the vertices array is made of x and y coordinates only.
/// 3 if it is made of x, y and z coordinates.</param>
/// <returns>A list of integers.
/// They are indices into the vertices array.
/// Every 3 integers represent the corner vertices of a triangle.</returns>
let earcut(vertices: array<float>, holeIndices: array<int>, dimensions: int) : ResizeArray<int> =
    let triangles = ResizeArray<int>()
    if vertices.Length < dimensions * 3 then
        triangles
    else
        let hasHoles =  not (obj.ReferenceEquals(holeIndices, null))  && holeIndices.Length > 0
        let outerLen = if hasHoles then holeIndices.[0] * dimensions else vertices.Length
        let mutable outerNode = linkedList(vertices, 0, outerLen, dimensions, true)

        if isNull outerNode || outerNode.next === outerNode.prev then triangles
        else
            let mutable minX = 0.0
            let mutable minY = 0.0
            let mutable invSize = 0.0

            if hasHoles then outerNode <- eliminateHoles(vertices, holeIndices, outerNode, dimensions)

            // if the shape is not too simple, we'll use z-order curve hash later; calculate polygon bbox
            if vertices.Length > 80 * dimensions then
                minX <- vertices.[0]
                minY <- vertices.[1]
                let mutable maxX = minX
                let mutable maxY = minY

                let mutable i = dimensions
                while i < outerLen do
                    let x = vertices.[i]
                    let y = vertices.[i + 1]
                    if x < minX then minX <- x
                    if y < minY then minY <- y
                    if x > maxX then maxX <- x
                    if y > maxY then maxY <- y
                    i <- i + dimensions

                // minX, minY and invSize are later used to transform coords into integers for z-order calculation
                invSize <- max (maxX - minX) (maxY - minY)
                invSize <- if invSize <> 0.0 then 32767.0 / invSize else 0.0

            earcutLinked(outerNode, triangles, dimensions, minX, minY, invSize, 0)

            triangles

/// Utility function to verify correctness of the triangulation.
/// Returns a percentage difference between the polygon area and its triangulation area.
/// used to detect any significant errors in the triangulation process.
let deviation(vertices: array<float>, holeIndices: array<int>, dim: int, triangles: ResizeArray<int>) : float =
    let hasHoles = not (obj.ReferenceEquals(holeIndices, null)) && holeIndices.Length > 0
    let outerLen = if hasHoles then holeIndices.[0] * dim else vertices.Length

    let mutable polygonArea = abs(signedArea(vertices, 0, outerLen, dim))
    if hasHoles then
        for i = 0 to holeIndices.Length - 1 do
            let start = holeIndices.[i] * dim
            let end_ = if i < holeIndices.Length - 1 then holeIndices.[i + 1] * dim else vertices.Length
            polygonArea <- polygonArea - abs(signedArea(vertices, start, end_, dim))

    let mutable trianglesArea = 0.0
    let mutable i = 0
    while i < triangles.Count do
        let a = triangles.[i] * dim
        let b = triangles.[i + 1] * dim
        let c = triangles.[i + 2] * dim
        trianglesArea <- trianglesArea + abs(
            (vertices.[a] - vertices.[c]) * (vertices.[b + 1] - vertices.[a + 1]) -
            (vertices.[a] - vertices.[b]) * (vertices.[c + 1] - vertices.[a + 1]))
        i <- i + 3

    if polygonArea = 0.0 && trianglesArea = 0.0 then 0.0
    else abs((trianglesArea - polygonArea) / polygonArea)



/// Utility function to turn a polygon in a multi-dimensional array form (e.g. as in GeoJSON) into a form Earcut accepts
let flatten(data: float[][][])  =
    let vertices = ResizeArray<float>()
    let holes = ResizeArray<int>()
    let dimensions = data.[0].[0].Length
    let mutable holeIndex = 0
    let mutable prevLen = 0

    for ring in data do
        for p in ring do
            for d = 0 to dimensions - 1 do
                vertices.Add(p.[d])
        if prevLen > 0 then
            holeIndex <- holeIndex + prevLen
            holes.Add(holeIndex)
        prevLen <- ring.Length

    {|vertices = vertices.ToArray(); holes = holes.ToArray(); dimensions = dimensions|}

