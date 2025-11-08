![Logo](https://raw.githubusercontent.com/goswinr/Earcut/main/Src/logo.png)
# Earcut


[![Earcut on nuget.org](https://img.shields.io/nuget/v/Earcut)](https://www.nuget.org/packages/Earcut/)
[![Build Status](https://github.com/goswinr/Earcut/actions/workflows/build.yml/badge.svg)](https://github.com/goswinr/Earcut/actions/workflows/build.yml)
[![Docs Build Status](https://github.com/goswinr/Earcut/actions/workflows/docs.yml/badge.svg)](https://github.com/goswinr/Earcut/actions/workflows/docs.yml)
[![Test Status](https://github.com/goswinr/Earcut/actions/workflows/test.yml/badge.svg)](https://github.com/goswinr/Earcut/actions/workflows/test.yml)
[![license](https://img.shields.io/github/license/goswinr/Earcut)](LICENSE.md)
![code size](https://img.shields.io/github/languages/code-size/goswinr/Earcut.svg)

The fastest and smallest polygon triangulation library.
A standalone port of Mapbox's Earcut algorithm to F#.

https://github.com/mapbox/earcut

v3.0.2 from 2025-11-6


#### Status

Stable for .NET 4.7 and .NET 6.0 and JS via Fable.

[All tests](https://github.com/mapbox/earcut/blob/main/test/test.js) of the original JS version pass.

All relevant code is in  [Earcut.fs](https://github.com/goswinr/Euclid.Earcut/blob/main/Src/Earcut.fs).
It contains the ported code without any major changes to the original logic.
It has no dependencies.

#### Performance

The F# port has about the same performance as the original JS version when compiled back to JS with Fable.


#### The algorithm

The library implements a modified ear slicing algorithm,
optimized by [z-order curve](http://en.wikipedia.org/wiki/Z-order_curve) hashing
and extended to handle holes, twisted polygons, degeneracies and self-intersections
in a way that doesn't _guarantee_ correctness of triangulation,
but attempts to always produce acceptable results for practical data.

It's based on ideas from
[FIST: Fast Industrial-Strength Triangulation of Polygons](http://www.cosy.sbg.ac.at/~held/projects/triang/triang.html) by Martin Held
and [Triangulation by Ear Clipping](http://www.geometrictools.com/Documentation/TriangulationByEarClipping.pdf) by David Eberly.

#### Why another triangulation library?

The aim of the original mapbox Earcut project is to create a triangulation library
that is **fast enough for real-time triangulation in the browser**,
sacrificing triangulation quality for raw speed and simplicity,
while being robust enough to handle most practical datasets without crashing or producing garbage.

If you want to get correct triangulation even on very bad data with lots of self-intersections
and earcut is not precise enough, take a look at [libtess.js](https://github.com/brendankenny/libtess.js).

#### Usage

```js
let triangles = Earcut.earcut([| 10.;0.; 0.;50.; 60.;60.; 70.;10.|], [||], 2) // returns [1;0;3; 3;2;1]
```

Signature: `earcut(vertices[, holes, dimensions = 2])`.

* `vertices` is a flat array of vertex coordinates like `[x0,y0, x1,y1, x2,y2, ...]`.
* `holes` is an array of hole _indices_ if any
  (e.g. `[5, 8]` for a 12-vertex input would mean one hole with vertices 5 to 7 and another with 8 to 11).
* `dimensions` is the number of coordinates per vertex in the input array (`2` by default). Only two are used for triangulation (`x` and `y`), and the rest are ignored.

Each group of three vertex indices in the resulting array forms a triangle.

```js
// triangulating a polygon with a hole
earcut([0,0, 100,0, 100,100, 0,100,  20,20, 80,20, 80,80, 20,80], [4]);
// [3,0,4, 5,4,0, 3,4,7, 5,0,1, 2,3,7, 6,5,1, 2,7,6, 6,1,2]

// triangulating a polygon with 3d coords
earcut([10,0,1, 0,50,2, 60,60,3, 70,10,4], null, 3);
// [1,0,3, 3,2,1]
```

If you pass a single vertex as a hole, Earcut treats it as a Steiner point.

Note that Earcut is a **2D** triangulation algorithm, and handles 3D data as if it was projected onto the XY plane (with Z component ignored).

If your input is a multi-dimensional array (e.g. [GeoJSON Polygon](http://geojson.org/geojson-spec.html#polygon)),
you can convert it to the format expected by Earcut with `Earcut.flatten`:

```fsharp
let data = Earcut.flatten(geojson.geometry.coordinates)
let triangles = Earcut.earcut(data.vertices, data.holes, data.dimensions)
```

After getting a triangulation, you can verify its correctness with `Earcut.deviation`:

```fsharp
let deviation = Earcut.deviation(vertices, holes, dimensions, triangles)
```

Returns the relative difference between the total area of triangles and the area of the input polygon.
`0` means the triangulation is fully correct.


## Build for .NET 4.7 and 6.0

`dotnet build`


## Build to JS with Fable

if you don't have Fable installed yet run:

`dotnet tool install fable`

then build to JS with:

`dotnet fable`

## Run Tests

build to JS with `dotnet fable`
run tests with `node Test/test.js`

