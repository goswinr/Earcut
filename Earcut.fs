// a port of earcut.js

// https://github.com/mapbox/earcut

// https://github.com/mapbox/earcut/blob/934e54d8bed85387330b7acaa5d2f5ba30a460a3/src/earcut.js


// v3.2.4 with all commits up to 934e54d from 2026-10-23

// earcut.js is Copyright (c) 2026, Mapbox and ISC licensed, see LICENSE.md

module Earcut

open System.Collections.Generic

/// A vertex in a circular doubly linked list representing a polygon ring.
/// prev/next are always linked (set immediately after createNode);
/// prevZ/nextZ are the z-order list links and are null at the ends.
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
    /// z-order curve value; doubles as the owning block index during eliminateHoles
    mutable z: int
    /// previous nodes in z-order
    mutable prevZ: Node
    /// next nodes in z-order
    mutable nextZ: Node
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
    }

let inline internal (===) (a:Node) (b:Node) = obj.ReferenceEquals(a, b)

let inline internal (=!=) (a:Node) (b:Node) = not <| obj.ReferenceEquals(a, b)

let inline internal notNull (node: Node) : bool = not <| obj.ReferenceEquals(node, Unchecked.defaultof<Node>)

let inline internal isNull (node: Node) : bool = obj.ReferenceEquals(node, Unchecked.defaultof<Node>)


// 32-bit integer multiplication with wrap-around overflow (Math.imul in JS)
let inline internal imul (a: int) (b: int) : int =
    #if FABLE_COMPILER_JAVASCRIPT || FABLE_COMPILER_TYPESCRIPT
        Fable.Core.JsInterop.emitJsExpr (a, b) "Math.imul($0, $1)"
    #else
        a * b
    #endif


module internal Arr =
    // Unchecked array access for the hot loops.
    // Fable compiles arr.[i] to a call of item(i, arr) and
    // arr.[i] <- v to setItem(arr, i, v) from fable-library, which check the bounds.
    // since all arrays use it, it stays generic and V8 can optimize.
    // in array heavy algorithms, this can cause a 80% extra performance penalty

    let inline zeroCreateUndef (len:int) : Node [] = //
        #if RELEASE && (FABLE_COMPILER_JAVASCRIPT || FABLE_COMPILER_TYPESCRIPT)
            Fable.Core.JsInterop.emitJsExpr (len) "new Array($0)" // fable would call fill on zero created arrays
        #else
            Array.zeroCreate<Node> len
        #endif

    let inline getIdx (idx:int) (arr: 'T[]) : 'T=
        #if RELEASE && (FABLE_COMPILER_JAVASCRIPT || FABLE_COMPILER_TYPESCRIPT)
            Fable.Core.JsInterop.emitJsExpr (arr,idx) "$0[$1]"
        #else
            arr.[idx]
        #endif

    let inline setIdx (idx:int) (value:'T) (arr: 'T[]) : unit =
        #if RELEASE && (FABLE_COMPILER_JAVASCRIPT || FABLE_COMPILER_TYPESCRIPT)
            Fable.Core.JsInterop.emitJsStatement (arr,idx,value) "$0[$1] = $2"
        #else
            arr.[idx] <- value
        #endif

    let inline len (arr: 'T[]) : int =
        #if RELEASE && (FABLE_COMPILER_JAVASCRIPT || FABLE_COMPILER_TYPESCRIPT)
            Fable.Core.JsInterop.emitJsExpr (arr) "$0.length" // avoid call to count() in fable lib
        #else
            arr.Length
        #endif

module internal ILst =

    let inline getIdx (idx:int) (iLst: IList<'T>) : 'T=
        #if RELEASE && (FABLE_COMPILER_JAVASCRIPT || FABLE_COMPILER_TYPESCRIPT)
            Fable.Core.JsInterop.emitJsExpr (iLst,idx) "$0[$1]"
        #else
            iLst.[idx]
        #endif

    let inline setIdx (idx:int) (value:'T) (iLst: IList<'T>) : unit =
        #if RELEASE && (FABLE_COMPILER_JAVASCRIPT || FABLE_COMPILER_TYPESCRIPT)
            Fable.Core.JsInterop.emitJsStatement (iLst,idx,value) "$0[$1] = $2"
        #else
            iLst.[idx] <- value
        #endif

    let inline len (iLst: IList<'T>) : int =
        #if RELEASE && (FABLE_COMPILER_JAVASCRIPT || FABLE_COMPILER_TYPESCRIPT)
            Fable.Core.JsInterop.emitJsExpr (iLst) "$0.length" // avoid call to count() in fable lib
        #else
            iLst.Count
        #endif

module internal ResizeArr =

    let inline getIdx (idx:int) (resizeArray: ResizeArray<'T>) : 'T=
        #if RELEASE && (FABLE_COMPILER_JAVASCRIPT || FABLE_COMPILER_TYPESCRIPT)
            Fable.Core.JsInterop.emitJsExpr (resizeArray,idx) "$0[$1]"
        #else
            resizeArray.[idx]
        #endif

    let inline setIdx (idx:int) (value:'T) (resizeArray: ResizeArray<'T>) : unit =
        #if RELEASE && (FABLE_COMPILER_JAVASCRIPT || FABLE_COMPILER_TYPESCRIPT)
            Fable.Core.JsInterop.emitJsStatement (resizeArray,idx,value) "$0[$1] = $2"
        #else
            resizeArray.[idx] <- value
        #endif

    let inline len (resizeArray: ResizeArray<'T>) : int =
        #if RELEASE && (FABLE_COMPILER_JAVASCRIPT || FABLE_COMPILER_TYPESCRIPT)
            Fable.Core.JsInterop.emitJsExpr (resizeArray) "$0.length" // avoid call to count() in fable lib
        #else
            resizeArray.Count
        #endif

    let inline lastIdx  (resizeArray: ResizeArray<'T>) : int =
        #if RELEASE && (FABLE_COMPILER_JAVASCRIPT || FABLE_COMPILER_TYPESCRIPT)
            Fable.Core.JsInterop.emitJsExpr (resizeArray) "$0.length - 1" // avoid call to count() in fable lib
        #else
            resizeArray.Count - 1
        #endif

    let inline clear (arr: ResizeArray<'T>) : unit =
        #if RELEASE && (FABLE_COMPILER_JAVASCRIPT || FABLE_COMPILER_TYPESCRIPT)
            Fable.Core.JsInterop.emitJsStatement arr "$0.length = 0"
        #else
            arr.Clear()
        #endif

    let inline pop (arr: ResizeArray<'T>) : 'T =
        #if RELEASE && (FABLE_COMPILER_JAVASCRIPT || FABLE_COMPILER_TYPESCRIPT)
            Fable.Core.JsInterop.emitJsExpr (arr) "$0.pop()"
        #else
            let v = arr.[arr.Count - 1]
            arr.RemoveAt(arr.Count - 1)
            v
        #endif



// Block-bbox index for findHoleBridge (issue #183): one [minX,minY,maxX,maxY] bbox per K
// consecutive ring edges, in a flat float array, so the leftward-ray scan can skip whole
// blocks in O(1) instead of walking the entire merged ring. Grown append-only - the outer
// ring seeds it, then each merged hole appends a segment (head node, stop node, K-blocks
// over head..stop); independent segments, not a ring tiling, since splices land mid-ring.
// Buffers are sized once from the input upper bound and reused across calls.
//
// filterPoints only drops collinear/coincident points, so a stale bbox stays a conservative
// superset of its live edges (never a false skip); the scan skips dead nodes (p.prev.next =!=
// p) and lazily advances a dead stop. Blocks are scanned in append (not ring) order, so the
// chosen bridge can differ from the un-indexed code - a different but equally valid result.
[<Literal>]
let internal K = 16 // edges per block

/// All reusable scratch state for one triangulation/refinement call, cached per-thread on .NET.
/// A single Workspace instance must never be used by more than one thread at the same time;
/// see the "Thread safety" section in README.md.
[<NoComparison; NoEquality>]
type internal Workspace = {
    // single-vertex holes to preserve through filterPoints (steiner points); kept off the Node
    // shape since they're rare - the empty-set fast path means non-steiner inputs pay nothing
    steiners: HashSet<Node>

    // halves of split polygons waiting to be triangulated, drained by earcut in a loop so that the
    // call stack doesn't grow with the nesting depth of splits
    splitStack: ResizeArray<Node>

    // set by filterPoints when it removes nodes; read by earcutLinked's stall handler
    mutable filteredOut: bool

    // see the block-bbox index comment above K
    mutable blockBBox: float[] // [minX,minY,maxX,maxY] per block
    mutable numBlocks: int
    mutable blockHead: Node[] // first node of each block's segment
    mutable blockStop: Node[] // node just past each block's segment (exclusive walk bound)
    // true only while eliminateHoles merges holes, so filterPoints keeps the block index live
    mutable indexActive: bool

    // radix sort buffers: node references and parallel z-values, plus an 8-bit histogram
    sortArr: ResizeArray<Node>
    sortBuf: ResizeArray<Node>
    mutable zArr: int[]
    mutable zBuf: int[]
    counts: int[]

    // Reusable scratch for refine():
    //   he        = twin half-edge of each edge, or -1 on the polygon boundary
    //   hTable    = open-addressing hash, slot -> half-edge index, valid iff hStamp[slot] = gen
    //   edgeStamp = pending-in-stack flag, cleared when the edge is popped
    mutable edgeStack: int[]
    mutable he: int[]
    mutable hTable: int[]
    mutable hStamp: int[]
    mutable edgeStamp: int[]
    mutable hMask: int
    mutable gen: int
    }

let internal createWorkspace() : Workspace = {
    steiners = HashSet<Node>(HashIdentity.Reference)
    splitStack = ResizeArray<Node>()
    filteredOut = false
    blockBBox = [||]
    numBlocks = 0
    blockHead = [||]
    blockStop = [||]
    indexActive = false
    sortArr = ResizeArray<Node>()
    sortBuf = ResizeArray<Node>()
    zArr = [||]
    zBuf = [||]
    counts = Array.zeroCreate<int> 256
    edgeStack = [||]
    he = [||]
    hTable = [||]
    hStamp = [||]
    edgeStamp = [||]
    hMask = 0
    gen = 0
    }

#if FABLE_COMPILER
let private workspace = createWorkspace()
let internal getWorkspace() = workspace
#else
let private workspace = new System.Threading.ThreadLocal<Workspace>(fun () -> createWorkspace())
let internal getWorkspace() = workspace.Value
#endif

let internal buildBlockIndex(ws: Workspace, maxNodes: int, numHoles: int) : unit =
    let blockBBox = ws.blockBBox
    let blockHead = ws.blockHead
    // upper bound: every input node indexed once, +2 bridge nodes per hole, plus a partial
    // trailing block per appended segment (outer ring + one per hole)
    let maxBlocks = (maxNodes + 2 * numHoles + K - 1) / K + numHoles + 2
    // every slot below numBlocks is written by indexSegment before it is read, so no initialization is needed
    if blockBBox.Length < maxBlocks * 4 then ws.blockBBox <- Array.zeroCreate<float> (maxBlocks * 4)
    if blockHead.Length < maxBlocks then
        ws.blockHead <- Arr.zeroCreateUndef maxBlocks
        ws.blockStop <- Arr.zeroCreateUndef maxBlocks
    ws.numBlocks <- 0

// index the ring run head..stop (exclusive) as ceil(len / K) blocks; head === stop means
// the whole ring. each block's bbox covers both endpoints of every edge it owns.
let internal indexSegment(ws: Workspace, head: Node, stop: Node) : unit =
    let blockBBox = ws.blockBBox
    let blockHead = ws.blockHead
    let blockStop = ws.blockStop
    let mutable numBlocks = ws.numBlocks
    let mutable p = head
    let mutable continueOuter = true
    // do-while loop: execute once then check condition
    while continueOuter do
        let b = numBlocks
        numBlocks <- numBlocks + 1
        Arr.setIdx b p blockHead
        // seed from the block's first point, so the inner loop only has to expand for c:
        // each iteration's p is the previous iteration's c, already covered (mapbox/earcut#207)
        let mutable minX = p.x
        let mutable minY = p.y
        let mutable maxX = p.x
        let mutable maxY = p.y
        let mutable k = 0
        let mutable continueInner = true
        // do-while loop: execute once then check condition
        while continueInner do
            let c = p.next // edge p->c; bbox must bound both endpoints
            p.z <- b // reuse z as the owning block during eliminateHoles (see growBlock)
            if c.x < minX then minX <- c.x
            if c.x > maxX then maxX <- c.x
            if c.y < minY then minY <- c.y
            if c.y > maxY then maxY <- c.y
            p <- c
            k <- k + 1
            if not (k < K && p =!= stop) then continueInner <- false
        Arr.setIdx b p blockStop
        let g = b * 4
        Arr.setIdx g minX blockBBox
        Arr.setIdx (g + 1) minY blockBBox
        Arr.setIdx (g + 2) maxX blockBBox
        Arr.setIdx (g + 3) maxY blockBBox
        if p === stop then continueOuter <- false
    ws.numBlocks <- numBlocks

// when filterPoints heals an edge head->tail (removing the collinear node between them), the
// healed edge can extend past head's frozen block bbox if its old far endpoint lived in another
// block; grow head's block bbox to cover tail so the leftward-ray prune can't false-skip it.
let inline internal growBlock(blockBBox: float[], head: Node, tail: Node) : unit =
    let g = head.z * 4
    if tail.x < Arr.getIdx g blockBBox then Arr.setIdx g tail.x blockBBox
    if tail.y < Arr.getIdx (g + 1) blockBBox then Arr.setIdx (g + 1) tail.y blockBBox
    if tail.x > Arr.getIdx (g + 2) blockBBox then Arr.setIdx (g + 2) tail.x blockBBox
    if tail.y > Arr.getIdx (g + 3) blockBBox then Arr.setIdx (g + 3) tail.y blockBBox

// ensure the walk's exclusive bound is live so we don't overrun into other blocks
let internal liveBlockStop(blockStop: Node[], b: int) : Node =
    let mutable stop = Arr.getIdx b blockStop
    while stop.prev.next =!= stop do stop <- stop.next
    Arr.setIdx b stop blockStop
    stop

// the block's head node can be removed by filterPoints during merges; advance it to the next
// live node so the walk doesn't start on (and immediately terminate at) a dead node. For the
// single full-ring seed block (head === stop) the same forward advance keeps them equal, so the
// do-while still laps the whole ring instead of collapsing to an empty walk.
let internal liveBlockHead(blockHead: Node[], b: int) : Node =
    let mutable head = Arr.getIdx b blockHead
    while head.prev.next =!= head do head <- head.next
    Arr.setIdx b head blockHead
    head

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

    // The indexActive/growBlock check lives in filterPoints, which handles all removals
    // during hole merging, so removeNode does not need scratch state.

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

// for collinear points p, q, r, check if point q lies on segment pr
let inline internal onSegment(p: Node, q: Node, r: Node) : bool =
    q.x <= max p.x r.x && q.x >= min p.x r.x && q.y <= max p.y r.y && q.y >= min p.y r.y

// check if two segments intersect; includeBoundary includes collinear boundary touches
let inline internal intersects(p1: Node, q1: Node, p2: Node, q2: Node, includeBoundary: bool) : bool =
    let o1 = area(p1, q1, p2)
    let o2 = area(p1, q1, q2)
    let o3 = area(p2, q2, p1)
    let o4 = area(p2, q2, q1)

    if ((o1 > 0.0 && o2 < 0.0) || (o1 < 0.0 && o2 > 0.0)) && ((o3 > 0.0 && o4 < 0.0) || (o3 < 0.0 && o4 > 0.0)) then true // general case
    elif not includeBoundary then false
    elif o1 = 0.0 && onSegment(p1, p2, q1) then true // p1, q1 and p2 are collinear and p2 lies on p1q1
    elif o2 = 0.0 && onSegment(p1, q2, q1) then true // p1, q1 and q2 are collinear and q2 lies on p1q1
    elif o3 = 0.0 && onSegment(p2, p1, q2) then true // p2, q2 and p1 are collinear and p1 lies on p2q2
    elif o4 = 0.0 && onSegment(p2, q1, q2) then true // p2, q2 and q1 are collinear and q1 lies on p2q2
    else false

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
        let n = p.next
        if ((p.y > py) <> (n.y > py)) &&
            (px < (n.x - p.x) * (py - p.y) / (n.y - p.y) + p.x) then
            inside <- not inside
        p <- n
        if p === a then continueLoop <- false
    inside

// check if a polygon diagonal intersects any polygon segments
let inline internal intersectsPolygon(a: Node, b: Node) : bool =
    // diagonal bbox; an edge whose bbox can't overlap it can't intersect it, so
    // skip the orientation test for those (the common case - the diagonal is short)
    let minX = min a.x b.x
    let maxX = max a.x b.x
    let minY = min a.y b.y
    let maxY = max a.y b.y

    let mutable p = a
    let mutable result = false
    let mutable continueLoop = true
    // do-while loop: execute once then check condition
    while continueLoop do
        let n = p.next
        if (p.x > maxX && n.x > maxX) || (p.x < minX && n.x < minX) ||
           (p.y > maxY && n.y > maxY) || (p.y < minY && n.y < minY) then
            p <- n
            if p === a then continueLoop <- false
        elif p.i <> a.i && n.i <> a.i && p.i <> b.i && n.i <> b.i &&
            intersects(p, n, a, b, true) then
            result <- true
            continueLoop <- false
        else
            p <- n
            if p === a then continueLoop <- false
    result

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
    let zeroLength = equals(a, b) && area(a.prev, a, a.next) > 0.0 && area(b.prev, b, b.next) > 0.0 // degenerate case
    a.next.i <> b.i && (zeroLength || locallyInside(a, b) && locallyInside(b, a) && // locally visible
        (area(a.prev, a, b.prev) <> 0.0 || area(a, b.prev, b) <> 0.0)) && // no opposite-facing sectors
        not (intersectsPolygon(a, b)) && (zeroLength || middleInside(a, b)) // doesn't intersect other edges, diagonal inside polygon

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

// one LSD radix pass: stably scatter the first n nodes (and their z) from src to dst,
// bucketed by the 8-bit digit of z at the given bit shift
let internal radixPass(counts: int[], n: int, src: ResizeArray<Node>, srcZ: int[], dst: ResizeArray<Node>, dstZ: int[], shift: int) : unit =
    Array.fill counts 0 256 0
    for i = 0 to n - 1 do
        let d = (Arr.getIdx i srcZ >>> shift) &&& 0xff
        Arr.setIdx d (Arr.getIdx d counts + 1) counts
    // turn per-bucket counts into start offsets (prefix sum)
    let mutable sum = 0
    for b = 0 to 255 do
        let c = Arr.getIdx b counts
        Arr.setIdx b sum counts
        sum <- sum + c
    for i = 0 to n - 1 do
        let z = Arr.getIdx i srcZ
        let d = (z >>> shift) &&& 0xff
        let pos = Arr.getIdx d counts
        Arr.setIdx d (pos + 1) counts
        ResizeArr.setIdx pos (ResizeArr.getIdx i src) dst
        Arr.setIdx pos z dstZ

// sort the first n nodes of sortArr by z, in place: insertion sort for small n (cheaper
// than histogram setup), else LSD radix in four 8-bit passes (covering z's 30 bits)
let internal sortNodes(ws: Workspace, n: int) : unit =
    if n > 32 && ws.zArr.Length < n then
        ws.zArr <- Array.zeroCreate<int> n
        ws.zBuf <- Array.zeroCreate<int> n
        // Radix scatter writes by index, so capacity alone is not enough.
        while ws.sortBuf.Count < n do
            ws.sortBuf.Add(Unchecked.defaultof<Node>)
    let sortArr = ws.sortArr
    let counts = ws.counts
    let zArr = ws.zArr
    let zBuf = ws.zBuf
    let sortBuf = ws.sortBuf
    if n <= 32 then
        for i = 1 to n - 1 do
            let node = ResizeArr.getIdx i sortArr
            let z = node.z
            let mutable j = i - 1
            while j >= 0 && (ResizeArr.getIdx j sortArr).z > z do
                ResizeArr.setIdx (j + 1) (ResizeArr.getIdx j sortArr) sortArr
                j <- j - 1
            ResizeArr.setIdx (j + 1) node sortArr
    else
        for i = 0 to n - 1 do
            Arr.setIdx i ((ResizeArr.getIdx i sortArr).z) zArr

        // even pass count lands the sorted result back in sortArr
        radixPass(counts, n, sortArr, zArr, sortBuf, zBuf, 0)
        radixPass(counts, n, sortBuf, zBuf, sortArr, zArr, 8)
        radixPass(counts, n, sortArr, zArr, sortBuf, zBuf, 16)
        radixPass(counts, n, sortBuf, zBuf, sortArr, zArr, 24)

// interlink polygon nodes in z-order: collect into a reusable list, sort by z, relink
let internal indexCurve(ws: Workspace, start: Node, minX: float, minY: float, invSize: float) : unit =
    let sortArr = ws.sortArr
    ResizeArr.clear sortArr
    let mutable p = start
    let mutable continueLoop = true
    // do-while loop: execute once then check condition
    while continueLoop do
        // always (re)compute: z may still hold a block index left over from eliminateHoles
        p.z <- zOrder(p.x, p.y, minX, minY, invSize)
        sortArr.Add(p)
        p <- p.next
        if p === start then continueLoop <- false

    let n = sortArr.Count
    sortNodes(ws, n)

    let mutable prev = Unchecked.defaultof<Node>
    for i = 0 to n - 1 do
        let node = ResizeArr.getIdx i sortArr
        node.prevZ <- prev
        if notNull prev then prev.nextZ <- node
        prev <- node
    prev.nextZ <- Unchecked.defaultof<Node>

    // Drop node references while retaining storage for the next call.
    ResizeArr.clear sortArr
    if n > 32 then
        for i = 0 to n - 1 do
            ResizeArr.setIdx i (Unchecked.defaultof<Node>) ws.sortBuf

let inline internal signedArea(data: array<float>, start: int, end_: int, dim: int) : float =
    let mutable sum = 0.0
    let mutable i = start
    let mutable j = end_ - dim
    while i < end_ do
        sum <- sum + (Arr.getIdx j data - Arr.getIdx i data) * (Arr.getIdx (i + 1) data + Arr.getIdx (j + 1) data)
        j <- i
        i <- i + dim
    sum

// create a circular doubly linked list from polygon points in the specified winding order
let internal linkedList(data: array<float>, start: int, end_: int, dim: int, clockwise: bool) : Node =
    let mutable last = Unchecked.defaultof<Node>

    if clockwise = (signedArea(data, start, end_, dim) > 0.0) then
        let mutable i = start
        while i < end_ do
            last <- insertNode(i / dim, Arr.getIdx i data, Arr.getIdx (i + 1) data, last)
            i <- i + dim
    else
        let mutable i = end_ - dim
        while i >= start do
            last <- insertNode(i / dim, Arr.getIdx i data, Arr.getIdx (i + 1) data, last)
            i <- i - dim

    if notNull last && equals(last, last.next) then
        removeNode(last)
        last <- last.next

    last

// Remove collinear or coincident points; removability depends only on a node's immediate
// neighbors, so we sweep forward and re-check the predecessor after each removal. When ende
// equals start we sweep the whole ring, lapping until nothing is removable (the fixpoint the
// clipper needs). With a distinct ende we heal only the dirty window around a bridge/diagonal
// cut, stopping at ende rather than lapping - O(window) instead of O(ring).
let internal filterPoints(ws: Workspace, start: Node, ende: Node) : Node =
    let steiners = ws.steiners
    let indexActive = ws.indexActive
    let blockBBox = ws.blockBBox
    let mutable filteredOut = ws.filteredOut
    let full = ende === start

    let mutable ende = ende
    let mutable p = start
    let mutable again = false
    let mutable continueLoop = true
    // do-while loop: execute once then check condition
    while continueLoop do
        again <- false
        if p =!= p.next && (steiners.Count = 0 || not (steiners.Contains p)) &&
            (equals(p, p.next) || area(p.prev, p, p.next) = 0.0) then
            if full || p === ende then ende <- p.prev // pull the stop bound back past the removal
            filteredOut <- true
            removeNode(p)
            // keep the hole-bridge index's block bboxes covering the healed prev->next edge
            // indexActive is only true during hole merging, when all removals go through
            // filterPoints, so this check lives here to keep removeNode independent of scratch state.
            if indexActive then growBlock(blockBBox, p.prev, p.next)
            p <- p.prev         // re-check the predecessor
            again <- true
        elif full || p =!= ende then
            p <- p.next
            again <- not full   // local heal: keep looping until the sweep reaches ende
        continueLoop <- again || p =!= ende

    ws.filteredOut <- filteredOut
    ende

// Return null when filtering leaves the ring unchanged, for the clipper's stall handler.
let internal filterPointsIfChanged(ws: Workspace, start: Node) : Node =
    ws.filteredOut <- false
    let filtered = filterPoints(ws, start, start)
    if ws.filteredOut then filtered else Unchecked.defaultof<Node>

// check whether every vertex of a ring is strictly convex
let internal isConvex(start: Node) : bool =
    let mutable p = start
    let mutable convex = true
    let mutable continueLoop = true
    // do-while loop: execute once then check condition
    while continueLoop do
        if area(p.prev, p, p.next) >= 0.0 then
            convex <- false
            continueLoop <- false
        else
            p <- p.next
            continueLoop <- p =!= start
    convex

// check whether a polygon node forms a valid ear with adjacent nodes
let internal isEar(ear: Node) : bool =
    // reflex check (area(a, b, c) >= 0) is hoisted into the earcutLinked caller
    let a = ear.prev
    let b = ear
    let c = ear.next

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

    // make sure we don't have other points inside the potential ear
    let mutable p = c.next
    let mutable result = true
    while p =!= a && result do
        if p.x >= x0 && p.x <= x1 && p.y >= y0 && p.y <= y1 && not (ax = p.x && ay = p.y) &&
            pointInTriangle(ax, ay, bx, by, cx, cy, p.x, p.y) &&
            area(p.prev, p, p.next) >= 0.0 then
            result <- false
        p <- p.next
    result

let internal isEarHashed(ear: Node, minX: float, minY: float, invSize: float) : bool =
    // reflex check is hoisted into the earcutLinked caller (see isEar)
    let a = ear.prev
    let b = ear
    let c = ear.next

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

    let mutable result = true

    // look for points inside the triangle in decreasing z-order
    let mutable p = ear.prevZ
    while result && notNull p && p.z >= minZ do
        if p.x >= x0 && p.x <= x1 && p.y >= y0 && p.y <= y1 && p =!= c && not (ax = p.x && ay = p.y) &&
            pointInTriangle(ax, ay, bx, by, cx, cy, p.x, p.y) && area(p.prev, p, p.next) >= 0.0 then
            result <- false
        else
            p <- p.prevZ

    // look for points in increasing z-order
    let mutable n = ear.nextZ
    while result && notNull n && n.z <= maxZ do
        if n.x >= x0 && n.x <= x1 && n.y >= y0 && n.y <= y1 && n =!= c && not (ax = n.x && ay = n.y) &&
            pointInTriangle(ax, ay, bx, by, cx, cy, n.x, n.y) && area(n.prev, n, n.next) >= 0.0 then
            result <- false
        else
            n <- n.nextZ

    result

// go through all polygon nodes and cure small local self-intersections
let internal cureLocalIntersections(ws: Workspace, start: Node, triangles: ResizeArray<int>) : Node =
    let mutable p = start
    let mutable start = start
    let mutable cured = false
    let mutable continueLoop = true
    // do-while loop: execute once then check condition
    while continueLoop do
        let a = p.prev
        let b = p.next.next

        if intersects(a, p, p.next, b, false) && locallyInside(a, b) && locallyInside(b, a) then
            triangles.Add(a.i)
            triangles.Add(p.i)
            triangles.Add(b.i)

            // remove two nodes involved
            removeNode(p)
            removeNode(p.next)

            start <- b
            p <- b
            cured <- true

        p <- p.next
        continueLoop <- p =!= start

    if cured then filterPoints(ws, p, p) else p

// try splitting polygon into two and triangulate them independently
let internal splitEarcut(ws: Workspace, start: Node) : unit =
    // look for a valid diagonal that divides the polygon into two
    let mutable a = start
    let mutable outerContinue = true
    while outerContinue do
        let mutable b = a.next.next
        let mutable innerContinue = b =!= a.prev
        while innerContinue do
            if a.i <> b.i && isValidDiagonal(a, b) then
                // split the polygon in two by the diagonal
                let mutable c = splitPolygon(a, b)

                // filter colinear points around the cuts
                a <- filterPoints(ws, a, a.next)
                c <- filterPoints(ws, c, c.next)

                // queue both halves for earcut, a on top so it's triangulated first
                ws.splitStack.Add(c)
                ws.splitStack.Add(a)
                outerContinue <- false
                innerContinue <- false
            else
                b <- b.next
                innerContinue <- b =!= a.prev

        if outerContinue then
            a <- a.next
            outerContinue <- a =!= start

// main ear slicing loop which triangulates a polygon (given as a linked list)
let internal earcutLinked(ws: Workspace, ear: Node, triangles: ResizeArray<int>, minX: float, minY: float, invSize: float) : unit =
    // interlink polygon nodes in z-order
    if invSize <> 0.0 then indexCurve(ws, ear, minX, minY, invSize)

    let mutable ear = ear
    let mutable stop = ear
    let mutable cured = false
    // a strictly convex ring has no vertex that could block an ear, so skip the point checks until a candidate fails;
    // without this, a big convex ring is cut as one fan of long triangles and each hashed check scans O(n) points
    let mutable convex = invSize <> 0.0 && isConvex(ear)
    let mutable continueLoop = true

    // iterate through ears, slicing them one by one
    while continueLoop && ear.prev =!= ear.next do
        let prev = ear.prev
        let next = ear.next

        if area(prev, ear, next) < 0.0 &&
            (convex || (if invSize <> 0.0 then isEarHashed(ear, minX, minY, invSize) else isEar(ear))) then
            // cut off the triangle
            triangles.Add(prev.i)
            triangles.Add(ear.i)
            triangles.Add(next.i)

            removeNode(ear)
            ear <- next
            stop <- next
        else
            convex <- false
            ear <- next

            // if we looped through the whole remaining polygon and can't find any more ears
            if ear === stop then
                // try filtering collinear/coincident points and slicing again - repeat as long as
                // filtering actually removes nodes, since each removal can expose new ears
                let filtered = filterPointsIfChanged(ws, ear)
                if notNull filtered then
                    ear <- filtered
                    stop <- ear
                elif not cured then
                    // filtering is exhausted: cure small local self-intersections once, then retry
                    ear <- cureLocalIntersections(ws, ear, triangles)
                    stop <- ear
                    cured <- true
                else
                    // as a last resort, try splitting the remaining polygon into two
                    splitEarcut(ws, ear)
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

    if result > 0.0 then 1
    elif result < 0.0 then -1
    // NaN (equal points with a vertical edge making the slope infinite) must compare as equal:
    // the JS sort spec coerces a NaN comparator result to 0, keeping the stable input order
    else 0

// David Eberly's algorithm for finding a bridge between hole and outer polygon
let internal findHoleBridge(blockBBox: float[], numBlocks: int, blockHead: Node[], blockStop: Node[], hole: Node, outerNode: Node) : Node =
    let hx = hole.x
    let hy = hole.y
    let mutable qx = -infinity
    let mutable m = Unchecked.defaultof<Node>

    // find a segment intersected by a ray from the hole's leftmost point to the left;
    // segment's endpoint with lesser x will be potential connection point
    // unless they intersect at a vertex, then choose the vertex
    if equals(hole, outerNode) then outerNode
    else
        let mutable earlyReturn = Unchecked.defaultof<Node>
        let mutable hasEarlyReturn = false

        // scan blocks; skip any whose bbox can't hold a crossing that beats qx and lies left
        // of hx (the prune Morton order can't express - explicit per-axis [minY,maxY]/[minX,maxX])
        let mutable b = 0
        let mutable g = 0
        while not hasEarlyReturn && b < numBlocks do
            if not (hy < Arr.getIdx (g + 1) blockBBox || hy > Arr.getIdx (g + 3) blockBBox || Arr.getIdx g blockBBox > hx || Arr.getIdx (g + 2) blockBBox <= qx) then
                // ensure the walk's exclusive bound is live so we don't overrun into other blocks
                let stop = liveBlockStop(blockStop, b)

                let mutable p = liveBlockHead(blockHead, b)
                let mutable continueLoop = true
                // do-while loop: execute once then check condition
                while continueLoop do
                    if equals(hole, p.next) then
                        earlyReturn <- p.next
                        hasEarlyReturn <- true
                        continueLoop <- false
                    elif hy <= p.y && hy >= p.next.y && p.next.y <> p.y then
                        let x = p.x + (hy - p.y) * (p.next.x - p.x) / (p.next.y - p.y)
                        if x <= hx && x > qx then
                            qx <- x
                            m <- if p.x < p.next.x then p else p.next
                            if x = hx then
                                earlyReturn <- m // hole touches outer segment; pick leftmost endpoint
                                hasEarlyReturn <- true
                                continueLoop <- false
                    if continueLoop then
                        p <- p.next
                        if p === stop then continueLoop <- false
            b <- b + 1
            g <- g + 4

        if hasEarlyReturn then earlyReturn
        elif isNull m then Unchecked.defaultof<Node>
        else
            // look for points inside the triangle of hole point, segment intersection and endpoint;
            // if there are no points found, we have a valid connection;
            // otherwise choose the point of the minimum angle with the ray as connection point

            let mx = m.x
            let my = m.y
            let tminY = min hy my // the triangle's y span; x span is [mx, hx]
            let tmaxY = max hy my
            let mutable tanMin = infinity

            // scan the same blocks; skip any whose bbox can't overlap the triangle's [mx,hx]×[tminY,tmaxY] box
            let mutable b = 0
            let mutable g = 0
            while b < numBlocks do
                if not (Arr.getIdx (g + 2) blockBBox < mx || Arr.getIdx g blockBBox > hx || Arr.getIdx (g + 3) blockBBox < tminY || Arr.getIdx (g + 1) blockBBox > tmaxY) then
                    let stop = liveBlockStop(blockStop, b)

                    let mutable p = liveBlockHead(blockHead, b)
                    let mutable continueLoop = true
                    // do-while loop: execute once then check condition
                    while continueLoop do
                        if hx >= p.x && p.x >= mx && hx <> p.x &&
                            pointInTriangle((if hy < my then hx else qx), hy, mx, my, (if hy < my then qx else hx), hy, p.x, p.y) then

                            let tan = abs(hy - p.y) / (hx - p.x) // tangential

                            // if hole point sits on p's horizontal edge (T-junction touch): the bridge runs
                            // along that edge - locallyInside rejects it as collinear, but it's valid
                            if (locallyInside(p, hole) || (p.y = hy && p.next.y = hy && p.next.x > hx)) &&
                                (tan < tanMin || (tan = tanMin && (p.x > m.x || (p.x = m.x && sectorContainsSector(m, p))))) then
                                m <- p
                                tanMin <- tan

                        p <- p.next
                        if p === stop then continueLoop <- false
                b <- b + 1
                g <- g + 4

            m

// find a bridge between vertices that connects hole with an outer ring and link it
let internal eliminateHole(ws: Workspace, hole: Node, outerNode: Node) : Node =
    let blockBBox = ws.blockBBox
    let numBlocks = ws.numBlocks
    let blockHead = ws.blockHead
    let blockStop = ws.blockStop
    let bridge = findHoleBridge(blockBBox, numBlocks, blockHead, blockStop, hole, outerNode)
    if isNull bridge then
        outerNode
    else
        let bridgeReverse = splitPolygon(bridge, hole)

        // index the merged-in segment before filtering: in ring order the splice runs
        // bridge -> hole -> bridgeReverse -> bridge2 -> (bridge's old next), covering the
        // hole's edges and both new slit edges. filterPoints below only drops collinear /
        // coincident points, so these bboxes stay valid (conservative) supersets.
        let bridge2 = bridgeReverse.next
        indexSegment(ws, bridge, bridge2.next)

        // heal collinear/coincident points around the two new slit edges, but leave bridge2 alone: dropping it as collinear
        // would leave bridge lying on the healed edge, where a later hole could bridge into it from outside its sector
        filterPoints(ws, bridgeReverse.prev, bridgeReverse) |> ignore

        // likewise, if bridge lies strictly between its prev and hole, healing would drop it and leave bridge2 on the healed
        // edge; skip the heal then (a spike at bridge, e.g. a hole touching an outer edge, still needs it)
        let a = bridge.prev
        if area(a, bridge, hole) = 0.0 && (a.x - bridge.x) * (hole.x - bridge.x) + (a.y - bridge.y) * (hole.y - bridge.y) < 0.0 then
            bridge
        else
            filterPoints(ws, bridge, bridge.next)

// link every hole into the outer loop, producing a single-ring polygon without holes
let internal eliminateHoles(ws: Workspace, data: array<float>, holeIndices: array<int>, outerNode: Node, dim: int) : Node =
    let steiners = ws.steiners
    let queue = ResizeArray<Node>(holeIndices.Length)

    for i = 0 to holeIndices.Length - 1 do
        let start = Arr.getIdx i holeIndices * dim
        let end_ = if i < holeIndices.Length - 1 then Arr.getIdx (i + 1) holeIndices * dim else data.Length
        let list = linkedList(data, start, end_, dim, false)
        if list === list.next then
            steiners.Add(list) |> ignore
        queue.Add(getLeftmost(list))

    // Holes that compare as equal must keep their input order, as they do in upstream: the JS
    // Array.sort is stable. List.Sort on .NET is an unstable introsort, so sort stably there.
    #if FABLE_COMPILER
    queue.Sort compareXYSlope
    #else
    let queue = ResizeArray<Node>(Seq.sortWith compareXYSlope queue)
    #endif

    // block-bbox index for findHoleBridge, grown append-only as holes merge (see notes
    // above buildBlockIndex). Seed it with the outer ring, then append each merged hole.
    buildBlockIndex(ws, data.Length / dim, holeIndices.Length)
    indexSegment(ws, outerNode, outerNode)

    // process holes from left to right; indexActive lets filterPoints keep block bboxes live as
    // filterPoints heals edges during merges (see growBlock)
    ws.indexActive <- true
    let mutable outerNode = outerNode
    for i = 0 to queue.Count - 1 do
        outerNode <- eliminateHole(ws, ResizeArr.getIdx i queue, outerNode)
    ws.indexActive <- false

    // collapse collinear/coincident points across the whole merged ring once before clipping
    filterPoints(ws, outerNode, outerNode)


///<summary> Triangulates a polygon with holes, given as flat array of numbers.</summary>
///<param name="vertices">A array of vertex coordinates like [x0, y0, x1, y1, x2, y2, ...].</param>
///<param name="holeIndices">An array of hole starting indices in the vertices array.
/// This index refers to the actual point array. Not the flattened vertices array.
/// If you have the index in the flattened vertices array, you need to divide it by the dimensions parameter to get the correct index parameter.
///  Use `null` if there are no holes.</param>
///<param name="dimensions">The number of coordinates per vertex in the vertices array:
/// 2 if the vertices array is made of x and y coordinates only.
/// 3 if it is made of x, y and z coordinates.</param>
/// <returns>A list of integers.
/// They are indices into the points array.
/// so if you use the flattened vertices array, you need to multiply the index by the dimensions parameter to get the correct index in the vertices array.
/// e.g.:
/// <code>
/// x = xyz[i * dimensions]
/// y = xyz[i * dimensions + 1]
/// </code>
/// (if dimensions = 2)</returns>
let earcut(vertices: array<float>, holeIndices: array<int>, dimensions: int) : ResizeArray<int> =
    // not in upstream, where dim defaults to 2: with unchecked array access a step of 0 would never finish the vertex loops
    if dimensions < 2 then
        raise (System.ArgumentException $"Earcut.earcut: dimensions must be at least 2, but got {dimensions}.")
    let triangles = ResizeArray<int>()
    if vertices.Length < dimensions * 3 then
        triangles
    else
        let ws = getWorkspace()
        let steiners = ws.steiners
        let hasHoles =  not (obj.ReferenceEquals(holeIndices, null))  && holeIndices.Length > 0
        let outerLen = if hasHoles then Arr.getIdx 0 holeIndices * dimensions else vertices.Length
        if steiners.Count > 0 then steiners.Clear()

        let mutable outerNode = linkedList(vertices, 0, outerLen, dimensions, true)

        if isNull outerNode || outerNode.next === outerNode.prev then
            triangles
        else
            let mutable minX = 0.0
            let mutable minY = 0.0
            let mutable invSize = 0.0

            if hasHoles then
                outerNode <- eliminateHoles(ws, vertices, holeIndices, outerNode, dimensions)

            // if the shape is not too simple, we'll use z-order curve hash later; calculate polygon bbox
            if vertices.Length > 80 * dimensions then
                minX <- Arr.getIdx 0 vertices
                minY <- Arr.getIdx 1 vertices
                let mutable maxX = minX
                let mutable maxY = minY

                let mutable i = dimensions
                while i < outerLen do
                    let x = Arr.getIdx i vertices
                    let y = Arr.getIdx (i + 1) vertices
                    if x < minX then minX <- x
                    if y < minY then minY <- y
                    if x > maxX then maxX <- x
                    if y > maxY then maxY <- y
                    i <- i + dimensions

                // minX, minY and invSize are later used to transform coords into integers for z-order calculation
                invSize <- max (maxX - minX) (maxY - minY)
                invSize <- if invSize <> 0.0 then 32767.0 / invSize else 0.0

            earcutLinked(ws, outerNode, triangles, minX, minY, invSize)
            let splitStack = ws.splitStack
            while splitStack.Count > 0 do
                earcutLinked(ws, ResizeArr.pop splitStack, triangles, minX, minY, invSize)

            triangles



/// Validates the input data for the earcut function.
/// Raises a descriptive System.ArgumentException if the input is invalid.
/// Checks include:
/// - dimensions is at least 2
/// - vertices array is not null or empty
/// - vertices array length is a multiple of dimensions
/// - no NaN or Infinity values in the vertices array
/// - the outer loop has at least 3 points
/// - each hole has at least 3 points (or 1 point for Steiner points)
/// - hole indices are within bounds and in ascending order
/// - the total area of all holes is smaller than the area of the outer loop
let validate(vertices: array<float>, holeIndices: array<int>, dimensions: int) : unit =

    if dimensions < 2 then
        raise (System.ArgumentException $"Earcut.validate: Dimensions must be at least 2, but got {dimensions}.")

    if obj.ReferenceEquals(vertices, null) || vertices.Length = 0 then
        raise (System.ArgumentException "Earcut.validate: Vertices array is null or empty.")

    if vertices.Length % dimensions <> 0 then
        raise (System.ArgumentException $"Earcut.validate: Vertices array length {vertices.Length} is not a multiple of dimensions {dimensions}.")

    let totalPoints = vertices.Length / dimensions

    for i = 0 to vertices.Length - 1 do
        if System.Double.IsNaN(Arr.getIdx i vertices) then
            raise (System.ArgumentException $"Earcut.validate: Vertices array contains a NaN value at index {i}.")
        if System.Double.IsInfinity(Arr.getIdx i vertices) then
            raise (System.ArgumentException $"Earcut.validate: Vertices array contains an Infinity value at index {i}.")

    let hasHoles = not (obj.ReferenceEquals(holeIndices, null)) && holeIndices.Length > 0

    let outerEnd = if hasHoles then Arr.getIdx 0 holeIndices else totalPoints

    if outerEnd < 3 then
        raise (System.ArgumentException $"Earcut.validate: Outer loop must have at least 3 points, but got {outerEnd}.")

    if hasHoles then
        for i = 0 to holeIndices.Length - 1 do
            let idx = Arr.getIdx i holeIndices
            if idx < 0 || idx > totalPoints then
                raise (System.ArgumentException $"Earcut.validate: Hole index {i} has value {idx} which is out of bounds [0, {totalPoints}].")
            if i > 0 && idx <= Arr.getIdx (i - 1) holeIndices then
                raise (System.ArgumentException $"Earcut.validate: Hole indices must be in ascending order, but index {i} ({idx}) is not greater than index {i - 1} ({holeIndices.[i - 1]}).")

        if Arr.getIdx (holeIndices.Length - 1) holeIndices >= totalPoints then
            raise (System.ArgumentException $"Earcut.validate: Last hole index {holeIndices.[holeIndices.Length - 1]} is at or beyond the end of the vertices array ({totalPoints} points).")

        for i = 0 to holeIndices.Length - 1 do
            let holeStart = Arr.getIdx i holeIndices
            let holeEnd = if i < holeIndices.Length - 1 then Arr.getIdx (i + 1) holeIndices else totalPoints
            let holePointCount = holeEnd - holeStart
            if holePointCount < 1 then
                raise (System.ArgumentException $"Earcut.validate: Hole {i} has {holePointCount} points, but must have at least 1.")
            if holePointCount = 2 then
                raise (System.ArgumentException $"Earcut.validate: Hole {i} has only 2 points. A hole needs at least 3 points, or 1 point for a Steiner point.")

        let outerArea = abs(signedArea(vertices, 0, outerEnd * dimensions, dimensions))
        let mutable holeAreaSum = 0.0
        for i = 0 to holeIndices.Length - 1 do
            let start = Arr.getIdx i holeIndices * dimensions
            let end_ = if i < holeIndices.Length - 1 then Arr.getIdx (i + 1) holeIndices * dimensions else vertices.Length
            holeAreaSum <- holeAreaSum + abs(signedArea(vertices, start, end_, dimensions))

        if holeAreaSum >= outerArea && outerArea > 0.0 then
            raise (System.ArgumentException $"Earcut.validate: Total area of holes ({holeAreaSum}) is not smaller than the area of the outer loop ({outerArea}).")

/// Utility function to verify correctness of the triangulation.
/// Returns the relative difference between the polygon area and its triangulation area (0.0 means they match, 0.5 means 50% off).
/// Used to detect any significant errors in the triangulation process.
let deviation(vertices: array<float>, holeIndices: array<int>, dim: int, triangles: ResizeArray<int>) : float =
    // not in upstream: with unchecked array access a step of 0 would never finish the vertex loops
    if dim < 2 then
        raise (System.ArgumentException $"Earcut.deviation: dim must be at least 2, but got {dim}.")
    let hasHoles = not (obj.ReferenceEquals(holeIndices, null)) && holeIndices.Length > 0
    let outerLen = if hasHoles then Arr.getIdx 0 holeIndices * dim else vertices.Length

    let mutable polygonArea = abs(signedArea(vertices, 0, outerLen, dim))
    if hasHoles then
        for i = 0 to holeIndices.Length - 1 do
            let start = Arr.getIdx i holeIndices * dim
            let end_ = if i < holeIndices.Length - 1 then Arr.getIdx (i + 1) holeIndices * dim else vertices.Length
            polygonArea <- polygonArea - abs(signedArea(vertices, start, end_, dim))

    let mutable trianglesArea = 0.0
    let mutable i = 0
    while i < triangles.Count do
        let a = ResizeArr.getIdx i triangles * dim
        let b = ResizeArr.getIdx (i + 1) triangles * dim
        let c = ResizeArr.getIdx (i + 2) triangles * dim
        trianglesArea <- trianglesArea + abs(
            (Arr.getIdx a vertices - Arr.getIdx c vertices) * (Arr.getIdx (b + 1) vertices - Arr.getIdx (a + 1) vertices) -
            (Arr.getIdx a vertices - Arr.getIdx b vertices) * (Arr.getIdx (c + 1) vertices - Arr.getIdx (a + 1) vertices))
        i <- i + 3

    if trianglesArea = 0.0 then
        // https://github.com/mapbox/earcut/commit/0302a7564198774d52d8763aacaf6c0f453f6f16
        // with no triangles, a polygon area within shoelace roundoff of zero (which scales with the
        // squared coordinate magnitude) means the input was degenerate rather than mistriangulated

        // The JS Number.EPSILON (2^-52) is not the same as System.Double.Epsilon, so spell it out for parity with upstream JS.
        let machineEpsilon = 2.220446049250313e-16
        let mutable maxCoordinate = 0.0
        let mutable i = 0
        while i < vertices.Length do
            maxCoordinate <- max maxCoordinate (abs(Arr.getIdx i vertices))
            maxCoordinate <- max maxCoordinate (abs(Arr.getIdx (i + 1) vertices))
            i <- i + dim
        if abs(polygonArea) <= float vertices.Length * maxCoordinate * maxCoordinate * machineEpsilon then
            0.0
        else
            1.0
    else abs((trianglesArea - polygonArea) / polygonArea)



/// Utility function to turn a polygon in a multi-dimensional array form (e.g. as in GeoJSON) into a form Earcut accepts
/// Returns an object with the following properties:
/// - vertices: a flat array of vertex coordinates like [x0, y0, x1, y1, x2, y2, ...].
/// - holes: an array of hole starting indices in the vertices array. Use `null` if there are no holes.
/// - dimensions: the number of coordinates per vertex in the vertices array. derived from the first vertex in the data array
/// (e.g. 2 if the vertices array is made of x and y coordinates only, 3 if it is made of x, y and z coordinates).
let flatten(data: float[][][])  =
    let vertices = ResizeArray<float>()
    let holes = ResizeArray<int>()
    let dimensions = data.[0].[0].Length
    let mutable holeIndex = 0

    // index loops instead of `for ring in data`, which Fable compiles to bounds-checked item() calls
    for r = 0 to data.Length - 1 do
        let ring = Arr.getIdx r data
        for i = 0 to ring.Length - 1 do
            let p = Arr.getIdx i ring
            for d = 0 to dimensions - 1 do
                vertices.Add(Arr.getIdx d p)
        if holeIndex > 0 && ring.Length > 0 then holes.Add(holeIndex) // skip empty rings
        holeIndex <- holeIndex + ring.Length

    {|vertices = vertices.ToArray(); holes = holes.ToArray(); dimensions = dimensions|}


let inline internal orient(ax: float, ay: float, bx: float, by: float, cx: float, cy: float) : float =
    (bx - ax) * (cy - ay) - (by - ay) * (cx - ax)

// Whether p is inside or exactly on the circumcircle of triangle (a, b, c). Sign is negated vs the
// usual predicate to match earcut's CCW winding - the standard sign would build the anti-Delaunay
// mesh. Cocircular quads are legal ties, so refine only flips when this returns false.
let inline internal inCircle(ax: float, ay: float, bx: float, by: float, cx: float, cy: float, px: float, py: float) : bool =
    let dx = ax - px
    let dy = ay - py
    let ex = bx - px
    let ey = by - py
    let fx = cx - px
    let fy = cy - py
    let ap = dx * dx + dy * dy
    let bp = ex * ex + ey * ey
    let cp = fx * fx + fy * fy
    // A near-cocircular quad is a legal Delaunay tie, but roundoff can flag both an edge and its
    // flip as illegal, cascading into an endless flip loop (#205) - so treat a determinant within
    // a small margin of zero as a tie. The determinant's worst-case roundoff error is provably
    // below 9e-16·(ap + bp + cp)² (Shewchuk-style bound), so the margin guarantees every executed
    // flip is illegal in exact arithmetic, and Lawson flipping always terminates.
    let s = ap + bp + cp
    dx * (ey * cp - bp * fy) - dy * (ex * cp - bp * fx) + ap * (ex * fy - ey * fx) <= 1e-13 * s * s

// next half-edge within the same triangle
let inline internal nextHE(e: int) : int =
    e - e % 3 + (e + 1) % 3

// Grow the scratch arrays on demand (like earcut's z-order arrays).
let internal ensureScratch(ws: Workspace, n: int) : unit =
    let edgeStack = ws.edgeStack
    let he = ws.he
    let edgeStamp = ws.edgeStamp
    let hTable = ws.hTable
    // edgeStack holds at most one entry per half-edge (edgeStamp dedups), so n is a safe cap -
    // sizing it up front lets the cascade push without a bounds/grow check.
    // edgeStamp and hStamp rely on starting out all-zero; the others are fully written before they are read.
    // Array.zeroCreate is used for all of them, Fable emits a (zero-filled) typed array for numeric arrays anyway
    if edgeStack.Length < n then ws.edgeStack <- Array.zeroCreate<int> n
    if he.Length < n then ws.he <- Array.zeroCreate<int> n
    if edgeStamp.Length < n then ws.edgeStamp <- Array.zeroCreate<int> n
    let mutable size = 1
    while size < n * 4 do size <- size <<< 1 // power-of-two table, load factor <= 0.25
    if hTable.Length < size then
        ws.hTable <- Array.zeroCreate<int> size
        ws.hStamp <- Array.zeroCreate<int> size
    ws.hMask <- size - 1

///<summary>Refines a triangulation toward the constrained Delaunay triangulation by legalizing every
/// interior edge in place with Lawson flips - maximizing the minimum angle and removing most
/// slivers. An optional post-pass for the output of the earcut function, or any manifold
/// triangle-index list indexing into coords. Adapted from delaunator's edge legalization.
/// Uses non-robust predicates: float input is fine, and the worst case is a not-quite-Delaunay
/// edge, never an invalid mesh.</summary>
///<param name="triangles">Triangle indices, as returned by the earcut function; mutated in place.</param>
///<param name="coords">The flat vertex coordinates passed to the earcut function.</param>
///<param name="dim">The number of coordinates per vertex in coords: 2 if it is made of x and y coordinates only.</param>
let refine(triangles: ResizeArray<int>, coords: array<float>, dim: int) : unit =
    let t = triangles
    let n = t.Count
    if n >= 6 then
        // the predicates below assume CCW triangles; for a CW mesh, swapping each edge's endpoints
        // mirrors them, so take the winding from the first non-degenerate triangle
        let mutable cw = false
        let mutable k = 0
        let mutable o = 0.0
        while o = 0.0 && k < n do
            let a = ResizeArr.getIdx k t * dim
            let b = ResizeArr.getIdx (k + 1) t * dim
            let c = ResizeArr.getIdx (k + 2) t * dim
            o <- orient(Arr.getIdx a coords, Arr.getIdx (a + 1) coords, Arr.getIdx b coords, Arr.getIdx (b + 1) coords, Arr.getIdx c coords, Arr.getIdx (c + 1) coords)
            cw <- o < 0.0
            k <- k + 3

        let ws = getWorkspace()
        ensureScratch(ws, n)
        let edgeStack = ws.edgeStack
        let he = ws.he
        let edgeStamp = ws.edgeStamp
        let hTable = ws.hTable
        let hStamp = ws.hStamp
        let hMask = ws.hMask
        let gen = ws.gen + 1
        ws.gen <- gen // bumping the generation logically empties the hash (no clearing)
        Array.fill he 0 n (-1)

        // Build half-edge twins with an undirected-edge hash; consumed slots mark linked pairs. As each
        // pair is linked we seed the stack with one representative (s, the earlier-inserted edge) - this
        // fuses the initial "push every interior edge" pass into the build, saving a full O(n) scan.
        // edgeStamp is all-zero here (balanced push/pop leaves it clean) and each pair links once, so
        // the seed write needs no dedup guard.
        let mutable i = 0
        for e = 0 to n - 1 do
            let a = ResizeArr.getIdx e t
            let b = ResizeArr.getIdx (nextHE(e)) t
            let lo = if a < b then a else b
            let hi = if a < b then b else a
            let mutable h = (imul lo (int 0x9e3779b1u) ^^^ imul hi (int 0x85ebca6bu)) &&& hMask
            let mutable searching = true
            while searching && Arr.getIdx h hStamp = gen do
                let s = Arr.getIdx h hTable
                // s = -1 marks a consumed slot (a pair already linked) - skip past it
                if s <> -1 then
                    let sa = ResizeArr.getIdx s t
                    let sb = ResizeArr.getIdx (nextHE(s)) t
                    if (sa = lo && sb = hi) || (sa = hi && sb = lo) then
                        Arr.setIdx e s he // link, then consume the slot
                        Arr.setIdx s e he
                        Arr.setIdx h (-1) hTable
                        Arr.setIdx s 1 edgeStamp // seed the interior edge for the cascade
                        Arr.setIdx i s edgeStack
                        i <- i + 1
                        searching <- false
                if searching then h <- (h + 1) &&& hMask
            if Arr.getIdx h hStamp <> gen then // first occurrence: insert
                Arr.setIdx h e hTable
                Arr.setIdx h gen hStamp

        while i > 0 do
            i <- i - 1
            let a = Arr.getIdx i edgeStack
            Arr.setIdx a 0 edgeStamp
            let b = Arr.getIdx a he
            if b <> -1 then
                let a0 = a - a % 3
                let b0 = b - b % 3
                let ar = a0 + (a + 2) % 3
                let al = a0 + (a + 1) % 3
                let bl = b0 + (b + 2) % 3
                let br = b0 + (b + 1) % 3
                let p0 = ResizeArr.getIdx ar t
                let pr = ResizeArr.getIdx (if cw then al else a) t
                let pl = ResizeArr.getIdx (if cw then a else al) t
                let p1 = ResizeArr.getIdx bl t

                let x0 = Arr.getIdx (p0 * dim) coords
                let y0 = Arr.getIdx (p0 * dim + 1) coords
                let xr = Arr.getIdx (pr * dim) coords
                let yr = Arr.getIdx (pr * dim + 1) coords
                let xl = Arr.getIdx (pl * dim) coords
                let yl = Arr.getIdx (pl * dim + 1) coords
                let x1 = Arr.getIdx (p1 * dim) coords
                let y1 = Arr.getIdx (p1 * dim + 1) coords

                // Test inCircle first: most interior edges are already Delaunay (inCircle true → no flip),
                // so this short-circuits before the two convexity orients on the common path. The quad must
                // also be convex (both new triangles CCW) - flipping a reflex quad would push a triangle
                // outside the polygon. Boundary/hole edges need no guard - they self-protect via he = -1.
                if not (inCircle(x0, y0, xr, yr, xl, yl, x1, y1)) &&
                    orient(x0, y0, xr, yr, x1, y1) > 0.0 && orient(x0, y0, x1, y1, xl, yl) > 0.0 then
                    ResizeArr.setIdx a p1 t
                    ResizeArr.setIdx b p0 t
                    let hbl = Arr.getIdx bl he
                    let har = Arr.getIdx ar he
                    Arr.setIdx a hbl he
                    if hbl <> -1 then Arr.setIdx hbl a he
                    Arr.setIdx b har he
                    if har <> -1 then Arr.setIdx har b he
                    Arr.setIdx ar bl he
                    Arr.setIdx bl ar he

                    // re-check the quad's four outer edges; skip boundary edges (he = -1) and any
                    // already queued (edgeStamp), which also keeps the stack bounded by n.
                    if hbl <> -1 && Arr.getIdx a edgeStamp = 0 then
                        Arr.setIdx a 1 edgeStamp
                        Arr.setIdx i a edgeStack
                        i <- i + 1
                    if har <> -1 && Arr.getIdx b edgeStamp = 0 then
                        Arr.setIdx b 1 edgeStamp
                        Arr.setIdx i b edgeStack
                        i <- i + 1
                    if Arr.getIdx al he <> -1 && Arr.getIdx al edgeStamp = 0 then
                        Arr.setIdx al 1 edgeStamp
                        Arr.setIdx i al edgeStack
                        i <- i + 1
                    if Arr.getIdx br he <> -1 && Arr.getIdx br edgeStamp = 0 then
                        Arr.setIdx br 1 edgeStamp
                        Arr.setIdx i br edgeStack
                        i <- i + 1


///<summary> Triangulates a polygon with holes, given as ILists (ResizeArray, array, ...) of flat x and y coordinates like [x0, y0, x1, y1, x2, y2, ...].</summary>
/// <param name="holes">An IList of holes, where each hole is an IList of flat x and y coordinates. Use `null` or empty array if there are no holes. Empty holes are skipped.</param>
/// <param name="boundary">An IList of flat x and y coordinates representing the outer polygon.</param>
/// <returns>A flat array of vertex coordinates like [x0, y0, x1, y1, x2, y2, ...] representing the triangulation of the polygon.
/// Every six consecutive values represent a triangle in 2D space.</returns>
let earcutTriangles(holes: IList<#IList<float>>) (boundary: IList<float>) : float[] =
    let holes = if holes = null then ResizeArray() :> IList<_> else holes
    let mutable size = ILst.len boundary
    for k = 0 to ILst.len holes - 1 do
        let hole = ILst.getIdx k holes
        size <- size + ILst.len hole

    let mutable ii = ILst.len boundary
    let holesIdx = ResizeArray<int>(ILst.len holes)
    let xys = Array.zeroCreate<float> size
    for i=0 to ILst.len boundary - 1 do
        Arr.setIdx i (ILst.getIdx i boundary) xys

    for j = 0 to ILst.len holes - 1 do
        let hole = ILst.getIdx j holes
        if ILst.len hole > 0 then // skip empty holes like flatten does, earcut can't handle a hole without points
            holesIdx.Add(ii/2) // divide by 2 because xys is a flat array of coordinates
            for k=0 to ILst.len hole - 1 do
                Arr.setIdx (ii+k) (ILst.getIdx k hole) xys
            ii <- ii + ILst.len hole

    let triaIdxs = earcut(xys, holesIdx.ToArray(), 2)

    let len = ResizeArr.len triaIdxs * 2
    let trias = Array.zeroCreate<float> len
    let mutable j = 0
    for i=0 to ResizeArr.lastIdx triaIdxs do
        let xIdx = ResizeArr.getIdx i triaIdxs * 2
        Arr.setIdx j (Arr.getIdx xIdx xys) trias
        j <- j + 1

        let yIdx = xIdx + 1
        Arr.setIdx j (Arr.getIdx yIdx xys) trias
        j <- j + 1
    trias

// The two SRTP wrappers below are public inline functions, which can't call the internal
// Arr / ILst / ResizeArr helpers, so they use plain indexing.

///<summary> Triangulates a polygon with holes, given as arrays of objects with x and y properties (lowercase).
/// Any object with x and y properties will work as a point object. (via F# statically resolved type parameters)</summary>
/// <param name="holes">An IList of holes, where each hole is an IList of points. Use `null` or empty array if there are no holes. Empty holes are skipped.</param>
/// <param name="pts">An IList of points representing the outer polygon.</param>
/// <returns>A flat array of vertex coordinates like [x0, y0, x1, y1, x2, y2, ...] representing the triangulation of the polygon.
/// Every six consecutive values represent a triangle in 2D space.</returns>
let inline earcutTrianglesFromMembersxy (holes: IList<#IList<'T>>) (pts: IList<'T>) : float[] when 'T : (member x: float) and 'T : (member y: float) =
    // TODO index get and set could be optimized using local inlined Arr / ResizeArr helpers
    let holes = if holes = null then ResizeArray() :> IList<_> else holes
    let mutable size = pts.Count * 2
    for hole in holes do
        size <- size + hole.Count * 2

    let mutable ii = 0
    let holesIdx = ResizeArray<int>(holes.Count)
    let xys = Array.zeroCreate<float> size
    for i=0 to pts.Count - 1 do
        let p = pts.[i]
        xys.[ii] <- p.x
        ii <- ii + 1
        xys.[ii] <- p.y
        ii <- ii + 1
    for j = 0 to holes.Count - 1 do
        let hole = holes.[j]
        if hole.Count > 0 then // skip empty holes like flatten does, earcut can't handle a hole without points
            holesIdx.Add(ii / 2) // divide by 2 because xys is a flat array of coordinates
            for k=0 to hole.Count - 1 do
                let p = hole.[k]
                xys.[ii] <- p.x
                ii <- ii + 1
                xys.[ii] <- p.y
                ii <- ii + 1
    let triaIdxs = earcut(xys, holesIdx.ToArray(), 2)

    let len = triaIdxs.Count * 2
    let trias = Array.zeroCreate<float> len
    let mutable j = 0
    for i=0 to triaIdxs.Count - 1 do
        let x = triaIdxs.[i] * 2
        trias.[j] <- xys.[x]
        j <- j + 1

        let y = x + 1
        trias.[j] <- xys.[y]
        j <- j + 1
    trias

///<summary> Triangulates a polygon with holes, given as arrays of objects with X and Y properties (Uppercase).
/// Any object with X and Y properties will work as a point object. (via F# statically resolved type parameters)</summary>
/// <param name="holes">An IList of holes, where each hole is an IList of points. Use `null` or empty array if there are no holes. Empty holes are skipped.</param>
/// <param name="pts">An IList of points representing the outer polygon.</param>
/// <returns>A flat array of vertex coordinates like [x0, y0, x1, y1, x2, y2, ...] representing the triangulation of the polygon.
/// Every six consecutive values represent a triangle in 2D space.</returns>
let inline earcutTrianglesFromMembersXY (holes: IList<#IList<'T>>) (pts: IList<'T>) : float[] when 'T : (member X: float) and 'T : (member Y: float) =
    // TODO index get and set could be optimized using local inlined Arr / ResizeArr helpers
    let holes = if holes = null then ResizeArray() :> IList<_> else holes
    let mutable size = pts.Count * 2
    for hole in holes do
        size <- size + hole.Count * 2

    let mutable ii = 0
    let holesIdx = ResizeArray<int>(holes.Count)
    let xys = Array.zeroCreate<float> size
    for i=0 to pts.Count - 1 do
        let p = pts.[i]
        xys.[ii] <- p.X
        ii <- ii + 1
        xys.[ii] <- p.Y
        ii <- ii + 1
    for j = 0 to holes.Count - 1 do
        let hole = holes.[j]
        if hole.Count > 0 then // skip empty holes like flatten does, earcut can't handle a hole without points
            holesIdx.Add(ii / 2) // divide by 2 because xys is a flat array of coordinates
            for k=0 to hole.Count - 1 do
                let p = hole.[k]
                xys.[ii] <- p.X
                ii <- ii + 1
                xys.[ii] <- p.Y
                ii <- ii + 1
    let triaIdxs = earcut(xys, holesIdx.ToArray(), 2)

    let len = triaIdxs.Count * 2
    let trias = Array.zeroCreate<float> len
    let mutable j = 0
    for i=0 to triaIdxs.Count - 1 do
        let x = triaIdxs.[i] * 2
        trias.[j] <- xys.[x]
        j <- j + 1

        let y = x + 1
        trias.[j] <- xys.[y]
        j <- j + 1
    trias
