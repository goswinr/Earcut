import earcut, {flatten} from '../../src/Earcut.fs.js';
// import earcut, {flatten} from '../../src/earcut.js';
import {readFileSync} from 'fs';

const data = JSON.parse(readFileSync(new URL('../test/fixtures/building.json', import.meta.url)));
const {vertices, holes} = flatten(data);

const start = performance.now();
let ops = 0;
let passed = 0;

do {
    earcut(vertices, holes);

    ops++;
    passed = performance.now() - start;
} while (passed < 1000);

console.log(`${Math.round(ops * 1000 / passed).toLocaleString()} ops/s`);



/*
original benchmark results:

PS D:\Git\_Euclid_\Earcut\Test\bench> node bench.js
typical OSM building (15 vertices): x 4,064,666 ops/sec ±0.29% (97 runs sampled)
dude shape (94 vertices): x 124,222 ops/sec ±0.38% (98 runs sampled)
dude shape with holes (104 vertices): x 98,384 ops/sec ±0.30% (99 runs sampled)
complex OSM water (2523 vertices): x 1,666 ops/sec ±0.39% (98 runs sampled)

F# to to Js benchmark results:

PS D:\Git\_Euclid_\Earcut\Test\bench> node bench.js
typical OSM building (15 vertices): x 4,043,920 ops/sec ±0.38% (92 runs sampled)
dude shape (94 vertices): x 125,380 ops/sec ±0.22% (95 runs sampled)
dude shape with holes (104 vertices): x 98,921 ops/sec ±0.49% (99 runs sampled)
complex OSM water (2523 vertices): x 1,672 ops/sec ±0.21% (98 runs sampled)
*/