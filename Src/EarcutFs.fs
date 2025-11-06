// a port of earcut.js

// https://github.com/mapbox/earcut

// https://github.com/mapbox/earcut/blob/54cbb0d38ece2441e510e61a40d187c8f6f514da/src/earcut.js

// v3.0.2 from 2025-11-6
module Earcut

open System
open System.Collections.Generic


type Node = {
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

let (*inline*) internal createNode(i: int, x: float, y: float) : Node =
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

let (*inline*) internal (===) (a:Node) (b:Node) = obj.ReferenceEquals(a, b)

let (*inline*) internal (!==) (a:Node) (b:Node) = not <| obj.ReferenceEquals(a, b)

let (*inline*) internal notNull (node: Node) : bool = not <| obj.ReferenceEquals(node, Unchecked.defaultof<Node>)

let (*inline*) internal isNull (node: Node) : bool = obj.ReferenceEquals(node, Unchecked.defaultof<Node>)

// create a node and optionally link it with previous one (in a circular doubly linked list)
let (*inline*) internal insertNode (i: int, x: float, y: float, last: Node) : Node =
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

let (*inline*) internal removeNode (p: Node) : unit =
    p.next.prev <- p.prev
    p.prev.next <- p.next

    if notNull p.prevZ then p.prevZ.nextZ <- p.nextZ
    if notNull p.nextZ then p.nextZ.prevZ <- p.prevZ

// signed area of a triangle
let (*inline*) internal area (p: Node, q: Node, r: Node) : float =
    (q.y - p.y) * (r.x - q.x) - (q.x - p.x) * (r.y - q.y)

// check if two points are equal
let (*inline*) internal equals (p1: Node, p2: Node) : bool =
    p1.x = p2.x && p1.y = p2.y
