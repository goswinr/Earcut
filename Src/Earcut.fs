// a port of earcut.js

// https://github.com/mapbox/earcut

// https://github.com/mapbox/earcut/blob/15928aef4dc8af0055186d17757da71940aff978/src/earcut.js

// v3.2.3 from 2026-07-01
module Earcut

open System.Collections.Generic

// NOTE: mirroring the upstream JS, this port keeps reusable scratch state (the steiner-point
// set, the hole-bridge block index, and the sort and refine buffers) off the polygon nodes so
// non-steiner / small / hole-free inputs pay nothing for it. Unlike the upstream JS, that scratch
// state lives in an internal Workspace object (see below) instead of at module level: a distinct
// cached Workspace per .NET thread lets independent, genuinely concurrent calls from different
// threads triangulate/refine in parallel without sharing state - see the "Thread safety" section
// in README.md for the exact guarantees and caveats (e.g. `refine` still mutates its caller-owned
// triangle list in place, so don't concurrently access/refine the very same list).

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

/// Creates an int array of the given length, initialized to zero. (an Int32Array when compiled with Fable)
let inline arrayZeroCreateInt (len:int) : int [] =
    #if FABLE_COMPILER_JAVASCRIPT || FABLE_COMPILER_TYPESCRIPT
        Fable.Core.JsInterop.emitJsExpr (len) "new Int32Array($0)"
    #else
        Array.zeroCreate<int> len
    #endif

/// Creates a float array of the given length, initialized to zero. (a Float64Array when compiled with Fable)
let inline arrayZeroCreateFloat (len:int) : float [] =
    #if FABLE_COMPILER_JAVASCRIPT || FABLE_COMPILER_TYPESCRIPT
        Fable.Core.JsInterop.emitJsExpr (len) "new Float64Array($0)"
    #else
        Array.zeroCreate<float> len
    #endif

// 32-bit integer multiplication with wrap-around overflow (Math.imul in JS)
let inline internal imul (a: int) (b: int) : int =
    #if FABLE_COMPILER_JAVASCRIPT || FABLE_COMPILER_TYPESCRIPT
        Fable.Core.JsInterop.emitJsExpr (a, b) "Math.imul($0, $1)"
    #else
        a * b
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

/// All reusable scratch state for one triangulation/refinement call, grouped so it can be
/// cached per-thread instead of living at module level (mirroring the upstream JS, which keeps
/// it module/global-scoped and is therefore not safe to call concurrently). A single Workspace
/// instance must never be used by more than one thread at the same time - see the "Thread
/// safety" section in README.md.
type internal Workspace() =
    // single-vertex holes to preserve through filterPoints (steiner points); kept off the Node
    // shape since they're rare - the empty-set fast path means non-steiner inputs pay nothing
    member val steiners = HashSet<Node>(HashIdentity.Reference)

    // set by filterPoints whenever it removes at least one node; read by earcutLinked's stall
    // handler to decide whether another clip pass is worth attempting before the costlier stages
    member val filteredOut = false with get, set

    // see the block-bbox index comment above K
    member val blockBBox : float[] = Array.empty with get, set // [minX,minY,maxX,maxY] per block
    member val numBlocks = 0 with get, set
    member val blockHead : Node[] = Array.empty with get, set // first node of each block's segment
    member val blockStop : Node[] = Array.empty with get, set // node just past each block's segment (exclusive walk bound)

    // true only while eliminateHoles merges holes, so removeNode keeps the block index live (growBlock)
    member val indexActive = false with get, set

    // scratch buffers reused across calls and grown on demand: two node-ref arrays that
    // ping-pong during the radix passes, plus parallel z-value arrays so the passes read
    // z from contiguous memory instead of dereferencing each node. 256-entry histogram for
    // 8-bit digits; the small histogram keeps per-call setup cheap (most rings are short)
    member val sortArr : Node[] = Array.empty with get, set
    member val sortBuf : Node[] = Array.empty with get, set
    member val zArr : int[] = Array.empty with get, set
    member val zBuf : int[] = Array.empty with get, set
    member val counts = arrayZeroCreateInt 256

    // Reusable scratch for refine():
    //   he        = twin half-edge of each edge, or -1 on the polygon boundary
    //   hTable    = open-addressing hash, slot -> half-edge index, valid iff hStamp[slot] = gen
    //   edgeStamp = pending-in-stack flag, cleared when the edge is popped
    member val edgeStack : int[] = Array.empty with get, set
    member val he : int[] = Array.empty with get, set
    member val hTable : int[] = Array.empty with get, set
    member val hStamp : int[] = Array.empty with get, set
    member val edgeStamp : int[] = Array.empty with get, set
    member val hMask = 0 with get, set
    member val gen = 0 with get, set

#if FABLE_COMPILER_JAVASCRIPT || FABLE_COMPILER_TYPESCRIPT
// JS is single-threaded, so one cached workspace is reused across calls on the (synchronous)
// execution path. Discarded and replaced on exception - see discardWorkspace below.
let mutable internal cachedWorkspace : Workspace = Unchecked.defaultof<Workspace>

let inline internal getWorkspace() : Workspace =
    if obj.ReferenceEquals(cachedWorkspace, null) then cachedWorkspace <- Workspace()
    cachedWorkspace

let inline internal discardWorkspace() : unit =
    cachedWorkspace <- Unchecked.defaultof<Workspace>
#else
// One lazily created Workspace cached per .NET thread, so independent threads can call earcut/
// refine genuinely in parallel: each gets its own scratch state, never shared across threads.
// Memory scales with the number of threads that have called in (each retains its grown buffers).
let internal threadWorkspace = new System.Threading.ThreadLocal<Workspace>(fun () -> Workspace())

let inline internal getWorkspace() : Workspace = threadWorkspace.Value

// On exception, indexActive / edgeStamp pending flags etc. may be left dirty (they're normally
// cleared only by successful completion), so discard this thread's workspace instead of adding
// clearing passes to every successful call.
let inline internal discardWorkspace() : unit =
    threadWorkspace.Value <- Workspace()
#endif

let internal buildBlockIndex(ws: Workspace, maxNodes: int, numHoles: int) : unit =
    // upper bound: every input node indexed once, +2 bridge nodes per hole, plus a partial
    // trailing block per appended segment (outer ring + one per hole)
    let maxBlocks = (maxNodes + 2 * numHoles + K - 1) / K + numHoles + 2
    if ws.blockBBox.Length < maxBlocks * 4 then ws.blockBBox <- arrayZeroCreateFloat (maxBlocks * 4)
    if ws.blockHead.Length < maxBlocks then
        ws.blockHead <- Array.zeroCreate maxBlocks
        ws.blockStop <- Array.zeroCreate maxBlocks
    ws.numBlocks <- 0

// index the ring run head..stop (exclusive) as ceil(len / K) blocks; head === stop means
// the whole ring. each block's bbox covers both endpoints of every edge it owns.
let internal indexSegment(ws: Workspace, head: Node, stop: Node) : unit =
    let mutable p = head
    let mutable continueOuter = true
    // do-while loop: execute once then check condition
    while continueOuter do
        let b = ws.numBlocks
        ws.numBlocks <- ws.numBlocks + 1
        ws.blockHead.[b] <- p
        let mutable minX = infinity
        let mutable minY = infinity
        let mutable maxX = -infinity
        let mutable maxY = -infinity
        let mutable k = 0
        let mutable continueInner = true
        // do-while loop: execute once then check condition
        while continueInner do
            let c = p.next // edge p->c; bbox must bound both endpoints
            p.z <- b // reuse z as the owning block during eliminateHoles (see growBlock)
            if p.x < minX then minX <- p.x
            if p.x > maxX then maxX <- p.x
            if p.y < minY then minY <- p.y
            if p.y > maxY then maxY <- p.y
            if c.x < minX then minX <- c.x
            if c.x > maxX then maxX <- c.x
            if c.y < minY then minY <- c.y
            if c.y > maxY then maxY <- c.y
            p <- c
            k <- k + 1
            if not (k < K && p =!= stop) then continueInner <- false
        ws.blockStop.[b] <- p
        let g = b * 4
        ws.blockBBox.[g] <- minX
        ws.blockBBox.[g + 1] <- minY
        ws.blockBBox.[g + 2] <- maxX
        ws.blockBBox.[g + 3] <- maxY
        if p === stop then continueOuter <- false

// when filterPoints heals an edge head->tail (removing the collinear node between them), the
// healed edge can extend past head's frozen block bbox if its old far endpoint lived in another
// block; grow head's block bbox to cover tail so the leftward-ray prune can't false-skip it.
let inline internal growBlock(ws: Workspace, head: Node, tail: Node) : unit =
    let g = head.z * 4
    if tail.x < ws.blockBBox.[g] then ws.blockBBox.[g] <- tail.x
    if tail.y < ws.blockBBox.[g + 1] then ws.blockBBox.[g + 1] <- tail.y
    if tail.x > ws.blockBBox.[g + 2] then ws.blockBBox.[g + 2] <- tail.x
    if tail.y > ws.blockBBox.[g + 3] then ws.blockBBox.[g + 3] <- tail.y

// ensure the walk's exclusive bound is live so we don't overrun into other blocks
let internal liveBlockStop(ws: Workspace, b: int) : Node =
    let mutable stop = ws.blockStop.[b]
    while stop.prev.next =!= stop do stop <- stop.next
    ws.blockStop.[b] <- stop
    stop

// the block's head node can be removed by filterPoints during merges; advance it to the next
// live node so the walk doesn't start on (and immediately terminate at) a dead node. For the
// single full-ring seed block (head === stop) the same forward advance keeps them equal, so the
// do-while still laps the whole ring instead of collapsing to an empty walk.
let internal liveBlockHead(ws: Workspace, b: int) : Node =
    let mutable head = ws.blockHead.[b]
    while head.prev.next =!= head do head <- head.next
    ws.blockHead.[b] <- head
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

let inline internal removeNode (ws: Workspace, p: Node) : unit =
    p.next.prev <- p.prev
    p.prev.next <- p.next

    if notNull p.prevZ then p.prevZ.nextZ <- p.nextZ
    if notNull p.nextZ then p.nextZ.prevZ <- p.prevZ

    // keep the hole-bridge index's block bboxes covering the healed prev->next edge
    if ws.indexActive then growBlock(ws, p.prev, p.next)

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
let internal radixPass(ws: Workspace, n: int, src: Node[], srcZ: int[], dst: Node[], dstZ: int[], shift: int) : unit =
    Array.fill ws.counts 0 256 0
    for i = 0 to n - 1 do
        let d = (srcZ.[i] >>> shift) &&& 0xff
        ws.counts.[d] <- ws.counts.[d] + 1
    // turn per-bucket counts into start offsets (prefix sum)
    let mutable sum = 0
    for b = 0 to 255 do
        let c = ws.counts.[b]
        ws.counts.[b] <- sum
        sum <- sum + c
    for i = 0 to n - 1 do
        let z = srcZ.[i]
        let d = (z >>> shift) &&& 0xff
        let pos = ws.counts.[d]
        ws.counts.[d] <- pos + 1
        dst.[pos] <- src.[i]
        dstZ.[pos] <- z

// sort the first n nodes of sortArr by z, in place: insertion sort for small n (cheaper
// than histogram setup), else LSD radix in four 8-bit passes (covering z's 30 bits)
let internal sortNodes(ws: Workspace, n: int) : unit =
    if n <= 32 then
        for i = 1 to n - 1 do
            let node = ws.sortArr.[i]
            let z = node.z
            let mutable j = i - 1
            while j >= 0 && ws.sortArr.[j].z > z do
                ws.sortArr.[j + 1] <- ws.sortArr.[j]
                j <- j - 1
            ws.sortArr.[j + 1] <- node
    else
        if ws.zArr.Length < n then
            ws.zArr <- arrayZeroCreateInt n
            ws.zBuf <- arrayZeroCreateInt n
            ws.sortBuf <- Array.zeroCreate n
        for i = 0 to n - 1 do
            ws.zArr.[i] <- ws.sortArr.[i].z

        // even pass count lands the sorted result back in sortArr
        radixPass(ws, n, ws.sortArr, ws.zArr, ws.sortBuf, ws.zBuf, 0)
        radixPass(ws, n, ws.sortBuf, ws.zBuf, ws.sortArr, ws.zArr, 8)
        radixPass(ws, n, ws.sortArr, ws.zArr, ws.sortBuf, ws.zBuf, 16)
        radixPass(ws, n, ws.sortBuf, ws.zBuf, ws.sortArr, ws.zArr, 24)

// interlink polygon nodes in z-order: collect into an array, sort by z, relink
let internal indexCurve(ws: Workspace, start: Node, minX: float, minY: float, invSize: float) : unit =
    let mutable p = start
    let mutable n = 0
    let mutable continueLoop = true
    // do-while loop: execute once then check condition
    while continueLoop do
        // always (re)compute: z may still hold a block index left over from eliminateHoles
        p.z <- zOrder(p.x, p.y, minX, minY, invSize)
        if n >= ws.sortArr.Length then
            // grow the reusable node array (the JS pushes into a plain array)
            let grown = Array.zeroCreate (max 256 (ws.sortArr.Length * 2))
            Array.blit ws.sortArr 0 grown 0 ws.sortArr.Length
            ws.sortArr <- grown
        ws.sortArr.[n] <- p
        n <- n + 1
        p <- p.next
        if p === start then continueLoop <- false

    sortNodes(ws, n)

    let mutable prev = Unchecked.defaultof<Node>
    for i = 0 to n - 1 do
        let node = ws.sortArr.[i]
        node.prevZ <- prev
        if notNull prev then prev.nextZ <- node
        prev <- node
    prev.nextZ <- Unchecked.defaultof<Node>

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
let internal linkedList(ws: Workspace, data: array<float>, start: int, end_: int, dim: int, clockwise: bool) : Node =
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
        removeNode(ws, last)
        last <- last.next

    last

// Remove collinear or coincident points; removability depends only on a node's immediate
// neighbors, so we sweep forward and re-check the predecessor after each removal. When ende
// equals start we sweep the whole ring, lapping until nothing is removable (the fixpoint the
// clipper needs). With a distinct ende we heal only the dirty window around a bridge/diagonal
// cut, stopping at ende rather than lapping - O(window) instead of O(ring).
let internal filterPoints(ws: Workspace, start: Node, ende: Node) : Node =
    let full = ende === start

    let mutable ende = ende
    let mutable p = start
    let mutable again = false
    let mutable continueLoop = true
    // do-while loop: execute once then check condition
    while continueLoop do
        again <- false
        if p =!= p.next && (ws.steiners.Count = 0 || not (ws.steiners.Contains p)) &&
            (equals(p, p.next) || area(p.prev, p, p.next) = 0.0) then
            if full || p === ende then ende <- p.prev // pull the stop bound back past the removal
            ws.filteredOut <- true
            removeNode(ws, p)
            p <- p.prev         // re-check the predecessor
            again <- true
        elif full || p =!= ende then
            p <- p.next
            again <- not full   // local heal: keep looping until the sweep reaches ende
        continueLoop <- again || p =!= ende

    ende

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
            removeNode(ws, p)
            removeNode(ws, p.next)

            start <- b
            p <- b
            cured <- true

        p <- p.next
        continueLoop <- p =!= start

    if cured then filterPoints(ws, p, p) else p

// try splitting polygon into two and triangulate them independently
let rec internal splitEarcut(ws: Workspace, start: Node, triangles: ResizeArray<int>, minX: float, minY: float, invSize: float) : unit =
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

                // run earcut on each half
                earcutLinked(ws, a, triangles, minX, minY, invSize)
                earcutLinked(ws, c, triangles, minX, minY, invSize)
                outerContinue <- false
                innerContinue <- false
            else
                b <- b.next
                innerContinue <- b =!= a.prev

        if outerContinue then
            a <- a.next
            outerContinue <- a =!= start

// main ear slicing loop which triangulates a polygon (given as a linked list)
and internal earcutLinked(ws: Workspace, ear: Node, triangles: ResizeArray<int>, minX: float, minY: float, invSize: float) : unit =
    // interlink polygon nodes in z-order
    if invSize <> 0.0 then indexCurve(ws, ear, minX, minY, invSize)

    let mutable ear = ear
    let mutable stop = ear
    let mutable cured = false
    let mutable continueLoop = true

    // iterate through ears, slicing them one by one
    while continueLoop && ear.prev =!= ear.next do
        let prev = ear.prev
        let next = ear.next

        if area(prev, ear, next) < 0.0 &&
            (if invSize <> 0.0 then isEarHashed(ear, minX, minY, invSize) else isEar(ear)) then
            // cut off the triangle
            triangles.Add(prev.i)
            triangles.Add(ear.i)
            triangles.Add(next.i)

            removeNode(ws, ear)
            ear <- next
            stop <- next
        else
            ear <- next

            // if we looped through the whole remaining polygon and can't find any more ears
            if ear === stop then
                // try filtering collinear/coincident points and slicing again - repeat as long as
                // filtering actually removes nodes, since each removal can expose new ears
                ws.filteredOut <- false
                ear <- filterPoints(ws, ear, ear)
                if ws.filteredOut then
                    stop <- ear
                elif not cured then
                    // filtering is exhausted: cure small local self-intersections once, then retry
                    ear <- cureLocalIntersections(ws, ear, triangles)
                    stop <- ear
                    cured <- true
                else
                    // as a last resort, try splitting the remaining polygon into two
                    splitEarcut(ws, ear, triangles, minX, minY, invSize)
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
let internal findHoleBridge(ws: Workspace, hole: Node, outerNode: Node) : Node =
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
        while not hasEarlyReturn && b < ws.numBlocks do
            if not (hy < ws.blockBBox.[g + 1] || hy > ws.blockBBox.[g + 3] || ws.blockBBox.[g] > hx || ws.blockBBox.[g + 2] <= qx) then
                // ensure the walk's exclusive bound is live so we don't overrun into other blocks
                let stop = liveBlockStop(ws, b)

                let mutable p = liveBlockHead(ws, b)
                let mutable continueLoop = true
                // do-while loop: execute once then check condition
                while continueLoop do
                    if p.prev.next === p then // skip nodes removed by filterPoints (stale in the index)
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
            while b < ws.numBlocks do
                if not (ws.blockBBox.[g + 2] < mx || ws.blockBBox.[g] > hx || ws.blockBBox.[g + 3] < tminY || ws.blockBBox.[g + 1] > tmaxY) then
                    let stop = liveBlockStop(ws, b)

                    let mutable p = liveBlockHead(ws, b)
                    let mutable continueLoop = true
                    // do-while loop: execute once then check condition
                    while continueLoop do
                        if p.prev.next === p && hx >= p.x && p.x >= mx && hx <> p.x && // skip dead nodes
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
    let bridge = findHoleBridge(ws, hole, outerNode)
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

        // heal collinear/coincident points around the two new slit edges
        filterPoints(ws, bridgeReverse, bridgeReverse.next) |> ignore
        filterPoints(ws, bridge, bridge.next)

// link every hole into the outer loop, producing a single-ring polygon without holes
let internal eliminateHoles(ws: Workspace, data: array<float>, holeIndices: array<int>, outerNode: Node, dim: int) : Node =
    let queue = ResizeArray<Node>()

    for i = 0 to holeIndices.Length - 1 do
        let start = holeIndices.[i] * dim
        let end_ = if i < holeIndices.Length - 1 then holeIndices.[i + 1] * dim else data.Length
        let list = linkedList(ws, data, start, end_, dim, false)
        if list === list.next then
            ws.steiners.Add(list) |> ignore
        queue.Add(getLeftmost(list))

    queue.Sort compareXYSlope

    // block-bbox index for findHoleBridge, grown append-only as holes merge (see notes
    // above buildBlockIndex). Seed it with the outer ring, then append each merged hole.
    buildBlockIndex(ws, data.Length / dim, holeIndices.Length)
    indexSegment(ws, outerNode, outerNode)

    // process holes from left to right; indexActive lets removeNode keep block bboxes live as
    // filterPoints heals edges during merges (see growBlock)
    ws.indexActive <- true
    let mutable outerNode = outerNode
    for i = 0 to queue.Count - 1 do
        outerNode <- eliminateHole(ws, queue.[i], outerNode)
    ws.indexActive <- false

    // collapse collinear/coincident points across the whole merged ring once before clipping
    filterPoints(ws, outerNode, outerNode)


let internal earcutWithWorkspace(ws: Workspace, vertices: array<float>, holeIndices: array<int>, dimensions: int) : ResizeArray<int> =
    let triangles = ResizeArray<int>()
    if vertices.Length < dimensions * 3 then
        triangles
    else
        let hasHoles =  not (obj.ReferenceEquals(holeIndices, null))  && holeIndices.Length > 0
        let outerLen = if hasHoles then holeIndices.[0] * dimensions else vertices.Length
        if ws.steiners.Count > 0 then ws.steiners.Clear()

        let mutable outerNode = linkedList(ws, vertices, 0, outerLen, dimensions, true)

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

            earcutLinked(ws, outerNode, triangles, minX, minY, invSize)

            triangles

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
/// <remarks> Thread safety: safe to call concurrently from independent .NET threads (each thread
/// uses its own cached scratch Workspace); do not mutate the vertices/holeIndices arrays while a
/// call using them is in progress. See the "Thread safety" section in README.md.</remarks>
let earcut(vertices: array<float>, holeIndices: array<int>, dimensions: int) : ResizeArray<int> =
    let ws = getWorkspace()
    try
        earcutWithWorkspace(ws, vertices, holeIndices, dimensions)
    with _ ->
        discardWorkspace()
        reraise()




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
        if System.Double.IsNaN(vertices.[i]) then
            raise (System.ArgumentException $"Earcut.validate: Vertices array contains a NaN value at index {i}.")
        if System.Double.IsInfinity(vertices.[i]) then
            raise (System.ArgumentException $"Earcut.validate: Vertices array contains an Infinity value at index {i}.")

    let hasHoles = not (obj.ReferenceEquals(holeIndices, null)) && holeIndices.Length > 0

    let outerEnd = if hasHoles then holeIndices.[0] else totalPoints

    if outerEnd < 3 then
        raise (System.ArgumentException $"Earcut.validate: Outer loop must have at least 3 points, but got {outerEnd}.")

    if hasHoles then
        for i = 0 to holeIndices.Length - 1 do
            let idx = holeIndices.[i]
            if idx < 0 || idx > totalPoints then
                raise (System.ArgumentException $"Earcut.validate: Hole index {i} has value {idx} which is out of bounds [0, {totalPoints}].")
            if i > 0 && idx <= holeIndices.[i - 1] then
                raise (System.ArgumentException $"Earcut.validate: Hole indices must be in ascending order, but index {i} ({idx}) is not greater than index {i - 1} ({holeIndices.[i - 1]}).")

        if holeIndices.[holeIndices.Length - 1] >= totalPoints then
            raise (System.ArgumentException $"Earcut.validate: Last hole index {holeIndices.[holeIndices.Length - 1]} is at or beyond the end of the vertices array ({totalPoints} points).")

        for i = 0 to holeIndices.Length - 1 do
            let holeStart = holeIndices.[i]
            let holeEnd = if i < holeIndices.Length - 1 then holeIndices.[i + 1] else totalPoints
            let holePointCount = holeEnd - holeStart
            if holePointCount < 1 then
                raise (System.ArgumentException $"Earcut.validate: Hole {i} has {holePointCount} points, but must have at least 1.")
            if holePointCount = 2 then
                raise (System.ArgumentException $"Earcut.validate: Hole {i} has only 2 points. A hole needs at least 3 points, or 1 point for a Steiner point.")

        let outerArea = abs(signedArea(vertices, 0, outerEnd * dimensions, dimensions))
        let mutable holeAreaSum = 0.0
        for i = 0 to holeIndices.Length - 1 do
            let start = holeIndices.[i] * dimensions
            let end_ = if i < holeIndices.Length - 1 then holeIndices.[i + 1] * dimensions else vertices.Length
            holeAreaSum <- holeAreaSum + abs(signedArea(vertices, start, end_, dimensions))

        if holeAreaSum >= outerArea && outerArea > 0.0 then
            raise (System.ArgumentException $"Earcut.validate: Total area of holes ({holeAreaSum}) is not smaller than the area of the outer loop ({outerArea}).")

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
    // edgeStack holds at most one entry per half-edge (edgeStamp dedups), so n is a safe cap -
    // sizing it up front lets the cascade push without a bounds/grow check.
    if ws.edgeStack.Length < n then ws.edgeStack <- arrayZeroCreateInt n
    if ws.he.Length < n then ws.he <- arrayZeroCreateInt n
    if ws.edgeStamp.Length < n then ws.edgeStamp <- arrayZeroCreateInt n
    let mutable size = 1
    while size < n * 4 do size <- size <<< 1 // power-of-two table, load factor <= 0.25
    if ws.hTable.Length < size then
        ws.hTable <- arrayZeroCreateInt size
        ws.hStamp <- arrayZeroCreateInt size
    ws.hMask <- size - 1

// core implementation of refine, operating on an explicit workspace - see the public refine
// wrapper below for the thread-safety-preserving entry point and full documentation
let internal refineWithWorkspace(ws: Workspace, triangles: ResizeArray<int>, coords: array<float>, dim: int) : unit =
    let t = triangles
    let n = t.Count
    if n >= 6 then
        ensureScratch(ws, n)
        ws.gen <- ws.gen + 1 // bumping the generation logically empties the hash (no clearing)
        Array.fill ws.he 0 n (-1)

        // Build half-edge twins with an undirected-edge hash; consumed slots mark linked pairs. As each
        // pair is linked we seed the stack with one representative (s, the earlier-inserted edge) - this
        // fuses the initial "push every interior edge" pass into the build, saving a full O(n) scan.
        // edgeStamp is all-zero here (balanced push/pop leaves it clean) and each pair links once, so
        // the seed write needs no dedup guard.
        let mutable i = 0
        for e = 0 to n - 1 do
            let a = t.[e]
            let b = t.[nextHE(e)]
            let lo = if a < b then a else b
            let hi = if a < b then b else a
            let mutable h = (imul lo (int 0x9e3779b1u) ^^^ imul hi (int 0x85ebca6bu)) &&& ws.hMask
            let mutable searching = true
            while searching && ws.hStamp.[h] = ws.gen do
                let s = ws.hTable.[h]
                // s = -1 marks a consumed slot (a pair already linked) - skip past it
                if s <> -1 then
                    let sa = t.[s]
                    let sb = t.[nextHE(s)]
                    if (sa = lo && sb = hi) || (sa = hi && sb = lo) then
                        ws.he.[e] <- s // link, then consume the slot
                        ws.he.[s] <- e
                        ws.hTable.[h] <- -1
                        ws.edgeStamp.[s] <- 1 // seed the interior edge for the cascade
                        ws.edgeStack.[i] <- s
                        i <- i + 1
                        searching <- false
                if searching then h <- (h + 1) &&& ws.hMask
            if ws.hStamp.[h] <> ws.gen then // first occurrence: insert
                ws.hTable.[h] <- e
                ws.hStamp.[h] <- ws.gen

        while i > 0 do
            i <- i - 1
            let a = ws.edgeStack.[i]
            ws.edgeStamp.[a] <- 0
            let b = ws.he.[a]
            if b <> -1 then
                let a0 = a - a % 3
                let b0 = b - b % 3
                let ar = a0 + (a + 2) % 3
                let al = a0 + (a + 1) % 3
                let bl = b0 + (b + 2) % 3
                let br = b0 + (b + 1) % 3
                let p0 = t.[ar]
                let pr = t.[a]
                let pl = t.[al]
                let p1 = t.[bl]

                let x0 = coords.[p0 * dim]
                let y0 = coords.[p0 * dim + 1]
                let xr = coords.[pr * dim]
                let yr = coords.[pr * dim + 1]
                let xl = coords.[pl * dim]
                let yl = coords.[pl * dim + 1]
                let x1 = coords.[p1 * dim]
                let y1 = coords.[p1 * dim + 1]

                // Test inCircle first: most interior edges are already Delaunay (inCircle true → no flip),
                // so this short-circuits before the two convexity orients on the common path. The quad must
                // also be convex (both new triangles CCW) - flipping a reflex quad would push a triangle
                // outside the polygon. Boundary/hole edges need no guard - they self-protect via he = -1.
                if not (inCircle(x0, y0, xr, yr, xl, yl, x1, y1)) &&
                    orient(x0, y0, xr, yr, x1, y1) > 0.0 && orient(x0, y0, x1, y1, xl, yl) > 0.0 then
                    t.[a] <- p1
                    t.[b] <- p0
                    let hbl = ws.he.[bl]
                    let har = ws.he.[ar]
                    ws.he.[a] <- hbl
                    if hbl <> -1 then ws.he.[hbl] <- a
                    ws.he.[b] <- har
                    if har <> -1 then ws.he.[har] <- b
                    ws.he.[ar] <- bl
                    ws.he.[bl] <- ar

                    // re-check the quad's four outer edges; skip boundary edges (he = -1) and any
                    // already queued (edgeStamp), which also keeps the stack bounded by n.
                    if hbl <> -1 && ws.edgeStamp.[a] = 0 then
                        ws.edgeStamp.[a] <- 1
                        ws.edgeStack.[i] <- a
                        i <- i + 1
                    if har <> -1 && ws.edgeStamp.[b] = 0 then
                        ws.edgeStamp.[b] <- 1
                        ws.edgeStack.[i] <- b
                        i <- i + 1
                    if ws.he.[al] <> -1 && ws.edgeStamp.[al] = 0 then
                        ws.edgeStamp.[al] <- 1
                        ws.edgeStack.[i] <- al
                        i <- i + 1
                    if ws.he.[br] <> -1 && ws.edgeStamp.[br] = 0 then
                        ws.edgeStamp.[br] <- 1
                        ws.edgeStack.[i] <- br
                        i <- i + 1

///<summary>Refines a triangulation toward the constrained Delaunay triangulation by legalizing every
/// interior edge in place with Lawson flips - maximizing the minimum angle and removing most
/// slivers. An optional post-pass for the output of the earcut function, or any manifold
/// triangle-index list indexing into coords. Adapted from delaunator's edge legalization.
/// Uses non-robust predicates: float input is fine, and the worst case is a not-quite-Delaunay
/// edge, never an invalid mesh.</summary>
///<param name="triangles">Triangle indices, as returned by the earcut function; mutated in place.</param>
///<param name="coords">The flat vertex coordinates passed to the earcut function.</param>
///<param name="dim">The number of coordinates per vertex in coords: 2 if it is made of x and y coordinates only.</param>
/// <remarks> Thread safety: safe to call concurrently from independent .NET threads (each thread
/// uses its own cached scratch Workspace). `refine` mutates the `triangles` list you pass in, in
/// place - do not concurrently access or refine the very same list from more than one thread, and
/// do not mutate `coords` while a call using it is in progress. See the "Thread safety" section
/// in README.md.</remarks>
let refine(triangles: ResizeArray<int>, coords: array<float>, dim: int) : unit =
    let ws = getWorkspace()
    try
        refineWithWorkspace(ws, triangles, coords, dim)
    with _ ->
        discardWorkspace()
        reraise()


///<summary> Triangulates a polygon with holes, given as ResizeArray flat X and Y coordinates.
/// Any object with x and y properties will work as a point object. (via F# statically resolved type parameters)</summary>
/// <param name="holes">An IList of holes, where each hole is an ResizeArray of x and y . Use `null` or empty array if there are no holes.</param>
/// <param name="boundary">An ResizeArray of x and y coordinates representing the outer polygon.</param>
/// <returns>A flat array of vertex coordinates like [x0, y0, x1, y1, x2, y2, ...] representing the triangulation of the polygon.
/// Every six consecutive values represent a triangle in 2D space.</returns>
let earcutTriangles(holes: IList<ResizeArray<float>>) (boundary: ResizeArray<float>) : float[] =
    let holes = if holes = null then ResizeArray() :> IList<_> else holes
    let mutable size = boundary.Count
    for k = 0 to holes.Count - 1 do
        let hole = holes.[k]
        size <- size + hole.Count

    let mutable ii = boundary.Count
    let holesIdx = arrayZeroCreateInt holes.Count
    let xys = arrayZeroCreateFloat size
    for i=0 to boundary.Count - 1 do
        xys.[i] <- boundary.[i]

    for j = 0 to holes.Count - 1 do
        let hole = holes.[j]
        holesIdx.[j] <- ii/2 // divide by 2 because xys is a flat array of coordinates
        for k=0 to hole.Count - 1 do
            xys.[ii+k] <- hole.[k]
        ii <- ii + hole.Count

    let triaIdxs = earcut(xys, holesIdx, 2)

    let len = triaIdxs.Count * 2
    let trias = arrayZeroCreateFloat len
    let mutable j = 0
    for i=0 to triaIdxs.Count - 1 do
        let xIdx = triaIdxs.[i] * 2
        trias.[j] <- xys.[xIdx]
        j <- j + 1

        let yIdx = xIdx + 1
        trias.[j] <- xys.[yIdx]
        j <- j + 1
    trias

///<summary> Triangulates a polygon with holes, given as arrays of objects wit x and y properties (lowercase).
/// Any object with x and y properties will work as a point object. (via F# statically resolved type parameters)</summary>
/// <param name="holes">An IList of holes, where each hole is an ResizeArray of points. Use `null` or empty array if there are no holes.</param>
/// <param name="pts">An ResizeArray of points representing the outer polygon.</param>
/// <returns>A flat array of vertex coordinates like [x0, y0, x1, y1, x2, y2, ...] representing the triangulation of the polygon.
/// Every six consecutive values represent a triangle in 2D space.</returns>
let inline earcutTrianglesFromMembersxy (holes: IList<ResizeArray<'T>>) (pts: ResizeArray<'T>) : float[] when 'T : (member x: float) and 'T : (member y: float) =
    let holes = if holes = null then ResizeArray() :> IList<_> else holes
    let mutable size = pts.Count * 2
    for hole in holes do
        size <- size + hole.Count * 2

    let mutable ii = 0
    let holesIdx = arrayZeroCreateInt holes.Count
    let xys = arrayZeroCreateFloat size
    for i=0 to pts.Count - 1 do
        let p = pts.[i]
        xys.[ii] <- p.x
        ii <- ii + 1
        xys.[ii] <- p.y
        ii <- ii + 1
    for j = 0 to holes.Count - 1 do
        let hole = holes.[j]
        holesIdx.[j] <- ii / 2 // divide by 2 because xys is a flat array of coordinates
        for k=0 to hole.Count - 1 do
            let p = hole.[k]
            xys.[ii] <- p.x
            ii <- ii + 1
            xys.[ii] <- p.y
            ii <- ii + 1
    let triaIdxs = earcut(xys, holesIdx, 2)

    let len = triaIdxs.Count * 2
    let trias = arrayZeroCreateFloat len
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
/// <param name="holes">An IList of holes, where each hole is an ResizeArray of points. Use `null` or empty array if there are no holes.</param>
/// <param name="pts">An ResizeArray of points representing the outer polygon.</param>
/// <returns>A flat array of vertex coordinates like [x0, y0, x1, y1, x2, y2, ...] representing the triangulation of the polygon.
/// Every six consecutive values represent a triangle in 2D space.</returns>
let inline earcutTrianglesFromMembersXY (holes: IList<ResizeArray<'T>>) (pts: ResizeArray<'T>) : float[] when 'T : (member X: float) and 'T : (member Y: float) =
    let holes = if holes = null then ResizeArray() :> IList<_> else holes
    let mutable size = pts.Count * 2
    for hole in holes do
        size <- size + hole.Count * 2

    let mutable ii = 0
    let holesIdx = arrayZeroCreateInt holes.Count
    let xys = arrayZeroCreateFloat size
    for i=0 to pts.Count - 1 do
        let p = pts.[i]
        xys.[ii] <- p.X
        ii <- ii + 1
        xys.[ii] <- p.Y
        ii <- ii + 1
    for j = 0 to holes.Count - 1 do
        let hole = holes.[j]
        holesIdx.[j] <- ii / 2 // divide by 2 because xys is a flat array of coordinates
        for k=0 to hole.Count - 1 do
            let p = hole.[k]
            xys.[ii] <- p.X
            ii <- ii + 1
            xys.[ii] <- p.Y
            ii <- ii + 1
    let triaIdxs = earcut(xys, holesIdx, 2)

    let len = triaIdxs.Count * 2
    let trias = arrayZeroCreateFloat len
    let mutable j = 0
    for i=0 to triaIdxs.Count - 1 do
        let x = triaIdxs.[i] * 2
        trias.[j] <- xys.[x]
        j <- j + 1

        let y = x + 1
        trias.[j] <- xys.[y]
        j <- j + 1
    trias
