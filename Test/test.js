
import test from 'node:test';
import assert from 'node:assert/strict';
import {Worker} from 'node:worker_threads';
import fs from 'fs';

// import earcut, {flatten, deviation, refine} from '../mapbox-earcut/src/earcut.js'; // original JS version
import {earcut, flatten, deviation, refine, validate, earcutTriangles} from '../Earcut.fs.js'; // F# to JS version
import {readTilesFixture} from './bench/tiles-fixture.js';

const expected = JSON.parse(fs.readFileSync(new URL('expected.json', import.meta.url)));

test('indices-2d', () => {
    const indices = earcut([10, 0, 0, 50, 60, 60, 70, 10], null, 2); // add dims for F# version
    assert.deepEqual(indices, [1, 0, 3, 1, 3, 2]);
});

test('indices-3d', () => {
    const indices = earcut([10, 0, 0, 0, 50, 0, 60, 60, 0, 70, 10, 0], null, 3);
    assert.deepEqual(indices, [1, 0, 3, 1, 3, 2]);
});

test('empty', () => {
    assert.deepEqual(earcut([], null, 2), []);
});

test('flatten skips empty rings', () => {
    const outer = [[0, 0], [10, 0], [10, 10], [0, 10]], hole = [[2, 2], [4, 2], [3, 4]];
    assert.deepEqual(flatten([outer, hole, []]), flatten([outer, hole]));
    const {vertices, holes, dimensions} = flatten([outer, []]);
    assert.deepEqual(earcut(vertices, holes, dimensions), earcut(flatten([outer]).vertices, null, 2)); // add dims for F# version
});

test('deviation zero-area handling', () => {
    const collinear = [0.1, 0.2, 1.3, 2.6, 2.5, 5.0, 3.7, 7.4];
    const collinearTriangles = earcut(collinear, null, 2);
    const valid = [0, 0, 1, 0, 1, 1, 0, 1];
    const zIgnored = [0, 0, 1e300, 1, 0, 1e300, 1, 1, 1e300, 0, 1, 1e300];
    assert.deepEqual(collinearTriangles, []);
    assert.equal(deviation(collinear, null, 2, collinearTriangles), 0);
    assert.equal(deviation([], null, 2, []), 0);
    assert.equal(deviation(valid, null, 2, []), 1);
    assert.equal(deviation(valid, null, 2, [0, 1, 2, 0, 2, 3]), 0);
    assert.equal(deviation(valid, null, 2, [0, 1, 2]), 0.5);
    assert.equal(deviation(zIgnored, null, 3, []), 1);
});

// tracks the worst deviation across the three non-zero rotations per fixture,
// so we can tell when the errors-with-rotation bound can be tightened
const maxRotated = new Map();

function trianglePerimeter(triangles, vertices, dim = 2) {
    let perimeter = 0;
    for (let i = 0; i < triangles.length; i += 3) {
        const ax = vertices[triangles[i] * dim],
            ay = vertices[triangles[i] * dim + 1],
            bx = vertices[triangles[i + 1] * dim],
            by = vertices[triangles[i + 1] * dim + 1],
            cx = vertices[triangles[i + 2] * dim],
            cy = vertices[triangles[i + 2] * dim + 1];

        perimeter += Math.hypot(ax - bx, ay - by) +
            Math.hypot(bx - cx, by - cy) +
            Math.hypot(cx - ax, cy - ay);
    }
    return perimeter;
}

function countIllegalEdges(triangles, vertices, dim = 2) {
    const halfEdges = new Int32Array(triangles.length);
    halfEdges.fill(-1);
    const edges = new Map();

    for (let e = 0; e < triangles.length; e++) {
        const a = triangles[e];
        const b = triangles[nextHalfEdge(e)];
        const key = a < b ? `${a},${b}` : `${b},${a}`;
        const twin = edges.get(key);
        if (twin !== undefined) {
            halfEdges[e] = twin;
            halfEdges[twin] = e;
            edges.delete(key);
        } else {
            edges.set(key, e);
        }
    }

    let illegal = 0;
    for (let a = 0; a < triangles.length; a++) {
        const b = halfEdges[a];
        if (b === -1 || a > b) continue;

        const a0 = a - a % 3;
        const b0 = b - b % 3;
        const ar = a0 + (a + 2) % 3;
        const al = a0 + (a + 1) % 3;
        const bl = b0 + (b + 2) % 3;
        const p0 = triangles[ar], pr = triangles[a], pl = triangles[al], p1 = triangles[bl];

        const x0 = vertices[p0 * dim], y0 = vertices[p0 * dim + 1];
        const xr = vertices[pr * dim], yr = vertices[pr * dim + 1];
        const xl = vertices[pl * dim], yl = vertices[pl * dim + 1];
        const x1 = vertices[p1 * dim], y1 = vertices[p1 * dim + 1];
        const convex = orient(x0, y0, xr, yr, x1, y1) > 0 && orient(x0, y0, x1, y1, xl, yl) > 0;

        if (convex && !inCircle(x0, y0, xr, yr, xl, yl, x1, y1)) illegal++;
    }
    return illegal;
}

function nextHalfEdge(e) {
    return e - e % 3 + (e + 1) % 3;
}

function orient(ax, ay, bx, by, cx, cy) {
    return (bx - ax) * (cy - ay) - (by - ay) * (cx - ax);
}

function inCircle(ax, ay, bx, by, cx, cy, px, py) {
    const dx = ax - px, dy = ay - py, ex = bx - px, ey = by - py, fx = cx - px, fy = cy - py;
    const ap = dx * dx + dy * dy, bp = ex * ex + ey * ey, cp = fx * fx + fy * fy;
    return dx * (ey * cp - bp * fy) - dy * (ex * cp - bp * fx) + ap * (ex * fy - ey * fx) <= 0;
}

for (const id of Object.keys(expected.triangles)) {

    for (const rotation of [0, 90, 180, 270]) {
        test(`${id} rotation ${rotation}`, (t) => {
            const coords = JSON.parse(fs.readFileSync(new URL(`fixtures/${id}.json`, import.meta.url)));
            const theta = rotation * Math.PI / 180;
            const xx = Math.round(Math.cos(theta));
            const xy = Math.round(-Math.sin(theta));
            const yx = Math.round(Math.sin(theta));
            const yy = Math.round(Math.cos(theta));
            if (rotation) {
                for (const ring of coords) {
                    for (const coord of ring) {
                        const [x, y] = coord;
                        coord[0] = xx * x + xy * y;
                        coord[1] = yx * x + yy * y;
                    }
                }
            }
            const data = flatten(coords);
            const indices = earcut(data.vertices, data.holes, data.dimensions);
            const err = deviation(data.vertices, data.holes, data.dimensions, indices);
            const expectedTriangles = expected.triangles[id];
            const expectedDeviation = (rotation !== 0 && expected['errors-with-rotation'][id]) || expected.errors[id] || 0;

            const numTriangles = indices.length / 3;
            if (rotation === 0) {
                assert.ok(numTriangles === expectedTriangles, `${numTriangles} triangles when expected ${expectedTriangles}`);
            }

            if (expectedTriangles > 0) {
                assert.ok(err <= expectedDeviation, `deviation ${err} <= ${expectedDeviation}`);
            }

            // surface fixtures whose deviation is well below the recorded threshold (at least 3x),
            // so improvements after a correctness fix are visible and the threshold can be tightened;
            // for rotations, compare the worst of the three against the shared errors-with-rotation bound
            if (rotation === 0) {
                if (expectedDeviation > 0 && err * 3 < expectedDeviation) {
                    t.diagnostic(`${id}: deviation ${err} < recorded ${expectedDeviation} (improved)`);
                }
            } else {
                maxRotated.set(id, Math.max(maxRotated.get(id) || 0, err));
                if (rotation === 270 && expectedDeviation > 0 && maxRotated.get(id) * 3 < expectedDeviation) {
                    t.diagnostic(`${id} rotated: max deviation ${maxRotated.get(id)} < recorded ${expectedDeviation} (improved)`);
                }
            }
        });
    }
}

test('infinite-loop', () => {
    earcut([1, 2, 2, 2, 1, 2, 1, 1, 1, 2, 4, 1, 5, 1, 3, 2, 4, 2, 4, 1], [5], 2);
});

test('large convex polygon', () => {
    // A strictly convex ring skips the point checks of ear slicing. Without this,
    // each hashed check of its fan of long triangles scans a large part of the ring.
    const n = 100000, vertices = [];
    for (let i = 0; i < n; i++) vertices.push(Math.cos(2 * Math.PI * i / n), Math.sin(2 * Math.PI * i / n));
    const triangles = earcut(vertices, null, 2);
    assert.equal(triangles.length / 3, n - 2);
    assert.ok(deviation(vertices, null, 2, triangles) < 1e-12);
});

test('nested splits do not overflow a small call stack', async () => {
    // a rectangle with k small triangular holes, each one step right and down from the previous; every hole adds a
    // level of nested splits. Runs in a worker with a fixed small stack (like a browser worker's) that recursive
    // splitting overflows from k ≈ 400; the worker code is inline because node --test runs every .js file in test/
    const url = new URL('../Earcut.fs.js', import.meta.url).href; // F# to JS version
    const worker = new Worker(`
        const {parentPort, workerData: {url, k}} = require('node:worker_threads');
        const vertices = [-1, -k, 2 * k + 1, -k, 2 * k + 1, 7, -1, 7], holes = [];
        for (let i = 0; i < k; i++) {
            holes.push(vertices.length / 2);
            vertices.push(1 + 2 * i, -i, 2 * i, 6 - i, 2 + 2 * i, 6 - i);
        }
        import(url).then(({earcut, deviation}) => {
            const triangles = earcut(vertices, holes, 2); // add dims for F# version
            parentPort.postMessage([triangles.length / 3, deviation(vertices, holes, 2, triangles)]);
        });
    `, {eval: true, workerData: {url, k: 600}, resourceLimits: {stackSizeMb: 0.35}});
    const [count, dev] = await new Promise((resolve, reject) => {
        worker.on('message', resolve).on('error', reject);
        worker.on('exit', code => reject(new Error(`worker exited with code ${code}`)));
    });
    assert.equal(count, 3002);
    assert.equal(dev, 0);
});

test('refine improves a bad quad diagonal', () => {
    const vertices = [0, 0, 3, 0, 10, 1, 0, 2];
    const triangles = [2, 3, 0, 2, 0, 1];
    const beforePerimeter = trianglePerimeter(triangles, vertices);
    refine(triangles, vertices, 2);
    const afterPerimeter = trianglePerimeter(triangles, vertices);

    assert.deepEqual(triangles, [2, 3, 1, 3, 0, 1]);
    assert.ok(afterPerimeter < beforePerimeter * 0.7);
    assert.equal(deviation(vertices, null, 2, triangles), 0);
});

test('refine improves a clockwise quad diagonal, keeping the winding', () => {
    const vertices = [0, 0, 3, 0, 10, 1, 0, 2];
    const triangles = [2, 0, 3, 2, 1, 0];
    refine(triangles, vertices, 2);

    assert.deepEqual(triangles, [1, 0, 3, 2, 1, 3]);
    assert.equal(deviation(vertices, null, 2, triangles), 0);
});

test('refine leaves a good quad diagonal alone', () => {
    const vertices = [0, 0, 5, 0, 4, 1, 0, 4];
    const triangles = [2, 3, 0, 2, 0, 1];

    refine(triangles, vertices, 2);

    assert.deepEqual(triangles, [2, 3, 0, 2, 0, 1]);
    assert.equal(deviation(vertices, null, 2, triangles), 0);
});

test('refine preserves a concave polygon', () => {
    const vertices = [0, 0, 4, 0, 4, 1, 1, 1, 1, 4, 0, 4];
    const triangles = earcut(vertices, null, 2);
    const length = triangles.length;
    const beforePerimeter = trianglePerimeter(triangles, vertices);
    refine(triangles, vertices, 2);
    const afterPerimeter = trianglePerimeter(triangles, vertices);

    assert.equal(triangles.length, length);
    assert.ok(afterPerimeter < beforePerimeter * 0.9);
    assert.equal(deviation(vertices, null, 2, triangles), 0);
});

test('refine terminates on near-cocircular points', {timeout: 5000}, () => {
    // Four near-cocircular points make the non-robust inCircle test give inconsistent signs for an
    // edge and its flip; without the tie margin the Lawson cascade flips one edge back and forth
    // forever. Should return with a valid mesh of unchanged size and zero deviation.
    const vertices = [
        127.65906365022843, 9.336137742499535, 124.21725103117963, 30.888097161477972,
        91.35514946628345, 89.65621376119454, 40.10446780041529, 121.5550560957686,
        -110.83205604043928, 64.03323632184248, -127.20394987965459, -14.253249980770189,
        61.074962259031416, -112.48932831632469, 127.37846573978545, -12.598669206638515,
        127.77010311801033, -7.668164657400608];
    const triangles = earcut(vertices, null, 2);
    const length = triangles.length;
    refine(triangles, vertices, 2);
    assert.equal(triangles.length, length);
    assert.ok(deviation(vertices, null, 2, triangles) < 1e-15);
});

test('refine legalizes all convex interior edges in earcut fixture', () => {
    const coords = JSON.parse(fs.readFileSync(new URL('fixtures/earcut.json', import.meta.url)));
    const data = flatten(coords);
    const triangles = earcut(data.vertices, data.holes, data.dimensions);

    refine(triangles, data.vertices, data.dimensions);

    assert.equal(countIllegalEdges(triangles, data.vertices, data.dimensions), 0);
    assert.equal(deviation(data.vertices, data.holes, data.dimensions, triangles), 0);
});

test('mvt fixture has zero deviation and refined quality', () => {
    const polys = readTilesFixture();
    let nonzero = 0;
    let firstIndex = -1;
    let firstDev = 0;
    let worstIndex = -1;
    let worstDev = 0;
    let sumDev = 0;
    let refinedNonzero = 0;
    let refinedFirstIndex = -1;
    let refinedFirstDev = 0;
    let refinedWorstIndex = -1;
    let refinedWorstDev = 0;
    let refinedSumDev = 0;
    let lengthChanged = 0;
    let basePerimeter = 0;
    let refinedPerimeter = 0;

    for (let i = 0; i < polys.length; i++) {
        const data = polys[i];
        const triangles = earcut(data.vertices, data.holes, data.dimensions);
        const length = triangles.length;
        basePerimeter += trianglePerimeter(triangles, data.vertices, data.dimensions);
        const dev = deviation(data.vertices, data.holes, data.dimensions, triangles);
        if (dev !== 0) {
            if (firstIndex < 0) {
                firstIndex = i;
                firstDev = dev;
            }
            nonzero++;
            sumDev += dev;
            if (dev > worstDev) {
                worstIndex = i;
                worstDev = dev;
            }
        }

        refine(triangles, data.vertices, data.dimensions);
        refinedPerimeter += trianglePerimeter(triangles, data.vertices, data.dimensions);
        if (triangles.length !== length) lengthChanged++;

        const refinedDev = deviation(data.vertices, data.holes, data.dimensions, triangles);
        if (refinedDev !== 0) {
            if (refinedFirstIndex < 0) {
                refinedFirstIndex = i;
                refinedFirstDev = refinedDev;
            }
            refinedNonzero++;
            refinedSumDev += refinedDev;
            if (refinedDev > refinedWorstDev) {
                refinedWorstIndex = i;
                refinedWorstDev = refinedDev;
            }
        }
    }

    assert.equal(polys.length, 119680);
    assert.equal(nonzero, 0,
        `${nonzero} polygons with nonzero deviation; first ${firstIndex}: ${firstDev}, ` +
        `worst ${worstIndex}: ${worstDev}, sum ${sumDev}`);

    assert.equal(lengthChanged, 0, `${lengthChanged} refined triangulations changed triangle count`);
    assert.equal(refinedNonzero, 0,
        `${refinedNonzero} refined polygons with nonzero deviation; first ${refinedFirstIndex}: ${refinedFirstDev}, ` +
        `worst ${refinedWorstIndex}: ${refinedWorstDev}, sum ${refinedSumDev}`);

    assert.ok(refinedPerimeter < basePerimeter * 0.72, `refined perimeter ratio ${refinedPerimeter / basePerimeter} < 0.72`);
});

// Regression for the hole-bridge block index (issue #183): a collinear-rich outer ring
// (integer grid, like MVT data) plus multiple holes used to drop a hole when filterPoints
// healed a collinear run across a block boundary, leaving the surviving edge outside its
// block's stale bbox so the leftward-ray scan false-skipped it. Assert full coverage.
test('block-index-collinear', () => {
    const N = 30;
    const outer = [];
    for (let x = 0; x <= N; x++) outer.push([x, 0]);
    for (let y = 1; y <= N; y++) outer.push([N, y]);
    for (let x = N - 1; x >= 0; x--) outer.push([x, N]);
    for (let y = N - 1; y >= 1; y--) outer.push([0, y]);
    const rect = (x0, y0, w, h) => [[x0, y0], [x0, y0 + h], [x0 + w, y0 + h], [x0 + w, y0]];
    const rings = [outer, rect(5, 5, 2, 4), rect(2, 23, 1, 1)];

    for (const rotation of [0, 90, 180, 270]) {
        const theta = rotation * Math.PI / 180;
        const xx = Math.round(Math.cos(theta)), xy = Math.round(-Math.sin(theta));
        const yx = Math.round(Math.sin(theta)), yy = Math.round(Math.cos(theta));
        const rotated = rings.map(ring => ring.map(([x, y]) => [xx * x + xy * y, yx * x + yy * y]));
        const data = flatten(rotated);
        const indices = earcut(data.vertices, data.holes, data.dimensions);
        const err = deviation(data.vertices, data.holes, data.dimensions, indices);
        assert.ok(err < 1e-9, `rotation ${rotation}: deviation ${err} (hole dropped?)`);
    }
});

// ============================================================
// Tests that are not in upstream: hole ordering and the F#-only API.
// The inline wrappers earcutTrianglesFromMembersxy / XY are not emitted to JS; Test/test.fsx covers them.
// ============================================================

// Holes that compare as equal in compareXYSlope must be merged in input order, as the stable JS
// sort does. On .NET an unstable sort used to reorder them from three holes on (and lose a triangle
// in the second case). The expected indices are the output of upstream earcut.js.
test('tied holes keep their input order', () => {
    const outer = [0, 0, 10, 0, 10, 10, 0, 10];
    // two Steiner points on the same spot, then one further left
    const steiners = [...outer, 5, 5, 5, 5, 2, 2];
    assert.deepEqual(earcut(steiners, [4, 5, 6], 2), [6, 0, 1, 5, 6, 1, 5, 1, 2, 5, 2, 3, 5, 3, 0]);
    // two triangles sharing their leftmost point and first edge direction, then one further left
    const triangles = [...outer, 5, 5, 6, 6, 6, 5, 5, 5, 7, 7, 7, 5, 2, 2, 3, 3, 3, 2];
    assert.deepEqual(earcut(triangles, [4, 7, 10], 2),
        [4, 11, 12, 12, 10, 0, 12, 0, 1, 4, 12, 1, 1, 8, 9, 4, 1, 9, 8, 1, 2, 8, 2, 3, 8, 3, 0]);
});

// With unchecked array access a coordinate step of 0 would loop forever, so it is rejected up front.
test('earcut and deviation reject dimensions below 2', () => {
    const square = [0, 0, 10, 0, 10, 10, 0, 10];
    assert.throws(() => earcut(square, null, 0), /dimensions must be at least 2/);
    assert.throws(() => earcut(square, null, 1), /dimensions must be at least 2/);
    assert.throws(() => deviation(square, null, 0, []), /dim must be at least 2/);
    assert.throws(() => deviation(square, null, 1, []), /dim must be at least 2/);
});

test('validate accepts well-formed input', () => {
    const square = [0, 0, 10, 0, 10, 10, 0, 10];
    validate(square, null, 2);
    validate(square, [], 2);
    validate([...square, 2, 2, 4, 2, 3, 4], [4], 2); // one hole
    validate([...square, 5, 5], [4], 2); // a single point hole is a Steiner point
    validate([0, 0, 1, 10, 0, 1, 10, 10, 1], null, 3);
});

test('validate rejects malformed input', () => {
    const square = [0, 0, 10, 0, 10, 10, 0, 10];
    const withHole = [...square, 2, 2, 4, 2, 3, 4]; // 7 points
    const withTwoHoles = [...withHole, 6, 6, 8, 6, 7, 8]; // 10 points
    assert.throws(() => validate(square, null, 1), /Dimensions must be at least 2/);
    assert.throws(() => validate(null, null, 2), /null or empty/);
    assert.throws(() => validate([], null, 2), /null or empty/);
    assert.throws(() => validate([...square, 1], null, 2), /not a multiple of dimensions/);
    assert.throws(() => validate([0, 0, NaN, 0, 10, 10], null, 2), /NaN value at index 2/);
    assert.throws(() => validate([0, 0, Infinity, 0, 10, 10], null, 2), /Infinity value at index 2/);
    assert.throws(() => validate(withHole, [2], 2), /Outer loop must have at least 3 points/);
    assert.throws(() => validate(withHole, [9], 2), /out of bounds/);
    assert.throws(() => validate(withTwoHoles, [7, 4], 2), /ascending order/);
    assert.throws(() => validate(withHole, [4, 7], 2), /at or beyond the end/);
    assert.throws(() => validate([...square, 2, 2, 4, 2], [4], 2), /has only 2 points/);
    assert.throws(() => validate([...square, -5, -5, 20, -5, 20, 20, -5, 20], [4], 2), /not smaller than the area of the outer loop/);
});

// summed area of the triangles in a flat [x0, y0, x1, y1, x2, y2, ...] coordinate array
function coordinateTrianglesArea(xys) {
    let area = 0;
    for (let i = 0; i < xys.length; i += 6) {
        area += Math.abs((xys[i + 2] - xys[i]) * (xys[i + 5] - xys[i + 1]) - (xys[i + 4] - xys[i]) * (xys[i + 3] - xys[i + 1])) / 2;
    }
    return area;
}

test('earcutTriangles returns triangle coordinates and skips empty holes', () => {
    const boundary = [0, 0, 10, 0, 10, 10, 0, 10];
    const hole = [2, 2, 4, 2, 4, 4, 2, 4];
    const plain = earcutTriangles(null, boundary);
    const holed = earcutTriangles([hole], boundary);
    assert.equal(plain.length, 2 * 6);
    assert.equal(coordinateTrianglesArea(plain), 100);
    assert.equal(holed.length, 8 * 6);
    assert.equal(coordinateTrianglesArea(holed), 96);
    assert.deepEqual(earcutTriangles([], boundary), plain);
    // an empty hole used to fail inside earcut
    assert.deepEqual(earcutTriangles([[]], boundary), plain);
    assert.deepEqual(earcutTriangles([[], hole, []], boundary), holed);
});
