// Large-ring benchmark for mapbox/earcut#212. Build first with dotnet fable.
// Run each before/after version in a fresh process: node Test/bench/bench-large.js
import assert from 'node:assert/strict';
import {earcut, deviation} from '../../Earcut.fs.js';

function radial(n, radius) {
    const vertices = [];
    for (let i = 0; i < n; i++) {
        const angle = 2 * Math.PI * i / n;
        const r = radius(i);
        vertices.push(r * Math.cos(angle), r * Math.sin(angle));
    }
    return vertices;
}

// Collinear boundary vertices must keep using the normal ear checks.
function rectangle(n) {
    const vertices = [];
    const corners = [[0, 0], [2, 0], [2, 1], [0, 1]];
    for (let side = 0; side < 4; side++) {
        const a = corners[side], b = corners[(side + 1) % 4];
        for (let i = 0; i < n / 4; i++) {
            const t = i / (n / 4);
            vertices.push(a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t);
        }
    }
    return vertices;
}

let seed = 212;
function randomRadius() {
    seed = (Math.imul(seed, 1664525) + 1013904223) >>> 0;
    return 0.85 + 0.3 * seed / 4294967296;
}

const cases = [
    ['regular 10k-gon', radial(10000, () => 1)],
    ['regular 100k-gon', radial(100000, () => 1)],
    ['star 10k', radial(10000, i => i % 2 ? 0.5 : 1)],
    ['random-radius 10k', radial(10000, randomRadius)],
    ['collinear rectangle 10k', rectangle(10000)]
];

console.log(`Node ${process.version}; 1 warmup + 5 measured calls per case; median ms/call`);
console.log('case                         median ms     min ms     max ms   triangles   checksum');
for (const [name, vertices] of cases) {
    const triangles = earcut(vertices, null, 2); // warmup and untimed validation
    assert.ok(deviation(vertices, null, 2, triangles) < 1e-9, `${name}: area deviation`);
    let checksum = 0;
    for (let i = 0; i < triangles.length; i++) checksum = (checksum + triangles[i] * (i + 1)) >>> 0;

    const times = [];
    for (let i = 0; i < 5; i++) {
        const start = performance.now();
        const result = earcut(vertices, null, 2);
        times.push(performance.now() - start);
        assert.equal(result.length, triangles.length);
    }
    times.sort((a, b) => a - b);
    console.log(`${name.padEnd(28)} ${times[2].toFixed(3).padStart(9)} ` +
        `${times[0].toFixed(3).padStart(10)} ${times[4].toFixed(3).padStart(10)} ` +
        `${String(triangles.length / 3).padStart(11)} ${String(checksum).padStart(10)}`);
}
