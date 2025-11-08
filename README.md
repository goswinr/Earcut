![Logo](https://raw.githubusercontent.com/goswinr/Earcut/main/Docs/img/logo128.png)

# Earcut


[![Earcut on nuget.org](https://img.shields.io/nuget/v/Earcut)](https://www.nuget.org/packages/Earcut/)
[![Build Status](https://github.com/goswinr/Earcut/actions/workflows/build.yml/badge.svg)](https://github.com/goswinr/Earcut/actions/workflows/build.yml)
[![Docs Build Status](https://github.com/goswinr/Earcut/actions/workflows/docs.yml/badge.svg)](https://github.com/goswinr/Earcut/actions/workflows/docs.yml)
[![Test Status](https://github.com/goswinr/Earcut/actions/workflows/test.yml/badge.svg)](https://github.com/goswinr/Earcut/actions/workflows/test.yml)
[![license](https://img.shields.io/github/license/goswinr/Earcut)](LICENSE.md)
![code size](https://img.shields.io/github/languages/code-size/goswinr/Earcut.svg)

The fastest and smallest polygon triangulation library. <br>
A port of Mapbox's Earcut algorithm to F#.

https://github.com/mapbox/earcut



### Status

Stable for .NET 4.7 and .NET 6.0 and JS via Fable.

v3.0.2 ported to F# on 2025-11-8

[All tests](https://github.com/mapbox/earcut/blob/main/test/test.js) of the original JS version pass.

All relevant code is in  [Earcut.fs](https://github.com/goswinr/Euclid.Earcut/blob/main/Src/Earcut.fs). <br>
It contains the ported code without any major changes to the original logic. <br>
It has no dependencies.

### Performance

The F# port has about the same performance as the original JS version when compiled back to JS with Fable.


### The algorithm

The library implements a modified ear slicing algorithm, <br>
optimized by [z-order curve](http://en.wikipedia.org/wiki/Z-order_curve) hashing <br>
and extended to handle holes, twisted polygons, degeneracies and self-intersections <br>
in a way that doesn't _guarantee_ correctness of triangulation, <br>
but attempts to always produce acceptable results for practical data. <br>

It's based on ideas from
[FIST: Fast Industrial-Strength Triangulation of Polygons](http://www.cosy.sbg.ac.at/~held/projects/triang/triang.html) by Martin Held <br>
and [Triangulation by Ear Clipping](http://www.geometrictools.com/Documentation/TriangulationByEarClipping.pdf) by David Eberly. <br>

### Why another triangulation library?

The aim of the original mapbox Earcut project is to create a triangulation library <br>
that is **fast enough for real-time triangulation in the browser**, <br>
sacrificing triangulation quality for raw speed and simplicity, <br>
while being robust enough to handle most practical datasets without crashing or producing garbage.

If you want to get correct triangulation even on very bad data with lots of self-intersections <br>
and earcut is not precise enough, take a look at [libtess.js](https://github.com/brendankenny/libtess.js).

### Usage

```fsharp
let triangles = Earcut.earcut([| 10.;0.; 0.;50.; 60.;60.; 70.;10.|], [||], 2) // returns [1;0;3; 3;2;1]
```

**Parameters:**

* `vertices` - A array of vertex coordinates like `[x0, y0, x1, y1, x2, y2, ...]`.
* `holeIndices` - An array of hole starting indices in the vertices array. Use `null` if there are no holes.<br>
  (e.g. `[5; 8]` for a 12-vertex (so 24 floats) input would mean one hole with vertices 5 to 7 and another with 8 to 11). <br>
  So a hole index of `5` means that the hole starts at `vertices[5 * dimensions]`. <br>
* `dimensions` - The number of coordinates per vertex in the vertices array:
  * `2` if the vertices array is made of x and y coordinates only.
  * `3` if it is made of x, y and z coordinates. <br>
Only the first two are used for triangulation (`x` and `y`), and the rest are ignored.

**Returns:**

A list of integers representing indices into the vertices array.<br>
Every 3 integers represent the corner vertices of a triangle.<br>
The actual indices can be retrieved by multiplying them with `dimensions`.<br>
So for a returned triangle `[0; 4; 7]` and `dimensions = 2`, the actual triangle vertices are at:<br>
`vertices[0 * 2]`, `vertices[0 * 2 + 1]`,<br>
`vertices[4 * 2]`, `vertices[4 * 2 + 1]`,<br>
`vertices[7 * 2]`, `vertices[7 * 2 + 1]`.


### Examples


```fsharp

// Triangulating a polygon with a hole
Earcut.earcut([|0.;0.;  100.;0.;  100.;100.;  0.;100.;  (* start of hole: *) 20.;20.;  80.;20.;  80.;80.;  20.;80.|], [|4|], 2)
// returns [0;4;7; 5;4;0; 3;0;7; 5;0;1; 2;3;7; 6;5;1; 2;7;6; 6;1;2]

// Triangulating a polygon with 3d coordinates
Earcut.earcut([|10.;0.;1.; 0.;50.;2.; 60.;60.;3.; 70.;10.;4.|], null, 3)
// returns [1;0;3; 3;2;1]
```

If you pass a single vertex as a hole, Earcut treats it as a Steiner point.

Note that Earcut is a **2D** triangulation algorithm, and handles 3D data as if it was projected onto the XY plane (with Z component ignored).

If your input is a multi-dimensional array (e.g. [GeoJSON Polygon](http://geojson.org/geojson-spec.html#polygon)), <br>
you can convert it to the format expected by Earcut with `Earcut.flatten`:

```fsharp
let data = Earcut.flatten(geojson.geometry.coordinates)
let triangles = Earcut.earcut(data.vertices, data.holes, data.dimensions)
```

After getting a triangulation, you can verify its correctness with `Earcut.deviation`:

```fsharp
let deviation = Earcut.deviation(vertices, holes, dimensions, triangles)
```

Returns the relative difference between the total area of triangles and the area of the input polygon. <br>
`0` means the triangulation is fully correct.


### Build for .NET 4.7 and 6.0

`dotnet build`


### Build to JS with Fable

If you don't have Fable installed yet run:

`dotnet tool install fable`

then build to JS with:

`dotnet fable`

### Run Tests

build to JS with `dotnet fable` <br>
run tests with `node Test/test.js`

### Images of test cases:

bad-diagonals:
![](Docs/testimg/bad-diagonals.png)

bad-hole:
![](Docs/testimg/bad-hole.png)

boxy:
![](Docs/testimg/boxy.png)

building:
![](Docs/testimg/building.png)

collinear-diagonal:
![](Docs/testimg/collinear-diagonal.png)

dude:
![](Docs/testimg/dude.png)

eberly-3:
![](Docs/testimg/eberly-3.png)

eberly-6:
![](Docs/testimg/eberly-6.png)

filtered-bridge-jhl:
![](Docs/testimg/filtered-bridge-jhl.png)

hilbert:
![](Docs/testimg/hilbert.png)

hole-touching-outer:
![](Docs/testimg/hole-touching-outer.png)

hourglass:
![](Docs/testimg/hourglass.png)

issue111:
![](Docs/testimg/issue111.png)

issue119:
![](Docs/testimg/issue119.png)

issue131:
![](Docs/testimg/issue131.png)

issue142:
![](Docs/testimg/issue142.png)

issue149:
![](Docs/testimg/issue149.png)

issue16:
![](Docs/testimg/issue16.png)

issue17:
![](Docs/testimg/issue17.png)

issue186:
![](Docs/testimg/issue186.png)

issue29:
![](Docs/testimg/issue29.png)

issue34:
![](Docs/testimg/issue34.png)

issue35:
![](Docs/testimg/issue35.png)

issue45:
![](Docs/testimg/issue45.png)

issue52:
![](Docs/testimg/issue52.png)

outside-ring:
![](Docs/testimg/outside-ring.png)

rain:
![](Docs/testimg/rain.png)

self-touching:
![](Docs/testimg/self-touching.png)

shared-points:
![](Docs/testimg/shared-points.png)

simplified-us-border:
![](Docs/testimg/simplified-us-border.png)

steiner:
![](Docs/testimg/steiner.png)

touching-holes:
![](Docs/testimg/touching-holes.png)

touching-holes2:
![](Docs/testimg/touching-holes2.png)

touching-holes3:
![](Docs/testimg/touching-holes3.png)

touching-holes4:
![](Docs/testimg/touching-holes4.png)

touching-holes5:
![](Docs/testimg/touching-holes5.png)

touching-holes6:
![](Docs/testimg/touching-holes6.png)

touching2:
![](Docs/testimg/touching2.png)

touching3:
![](Docs/testimg/touching3.png)

touching4:
![](Docs/testimg/touching4.png)

water-huge2:
![](Docs/testimg/water-huge2.png)

water:
![](Docs/testimg/water.png)

water2:
![](Docs/testimg/water2.png)

water3:
![](Docs/testimg/water3.png)

water3b:
![](Docs/testimg/water3b.png)

water4:
![](Docs/testimg/water4.png)