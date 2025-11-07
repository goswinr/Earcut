# Euclid.Earcut




A standalone port of Mapbox's Earcut library to F#.

https://github.com/mapbox/earcut

v3.0.2 from 2025-11-6


## Status

Stable for .NET 4.7 and .NET 6.0 and JS via Fable.

[All tests](https://github.com/mapbox/earcut/blob/main/test/test.js) of the original JS version at  pass.

The file [EarcutFs.fs](https://github.com/goswinr/Euclid.Earcut/blob/main/Src/EarcutFs.fs) contains the ported code without any major changes to the original logic.

It has no dependencies.

The file [Euclid.Earcut.fs](https://github.com/goswinr/Euclid.Earcut/blob/main/Src/Euclid.Earcut.fs) contains functions [Euclid's Polyline2D](https://github.com/goswinr/Euclid) class.

## Performance

The F# port compiled back to JS has about the same performance as the original JS version while running the tests.


## Build for .NET 4.7 and 6.0

`dotnet build`


## Build to JS with Fable

`dotnet fable`

## Run Tests

build to JS with `dotnet fable`
run tests with `node Test/test.js`

