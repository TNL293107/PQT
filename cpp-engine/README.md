# C++ Engine

Performance-sensitive components of the terminal.

**Phase 0 status:** toolchain only. There is no order book, no market data
decoder, and no execution logic here. The engine exists so the C++ build and
test path is proven before Phase 16 needs it.

## Layout

```
cpp-engine/
├── CMakeLists.txt          project, standard, warning policy
├── CMakePresets.json       debug / release / ci presets
├── include/pq/             public headers
├── src/                    library sources + generated version header
├── app/                    pq-engine executable
└── tests/                  GoogleTest suite, registered with CTest
```

## Build and test

```bash
cmake --preset ci && cmake --build --preset ci && ctest --preset ci
```

Presets:

| Preset    | Build type | Warnings as errors |
| --------- | ---------- | ------------------ |
| `debug`   | Debug      | no                 |
| `release` | Release    | no                 |
| `ci`      | Release    | yes                |

## Requirements

- CMake 3.24 or newer
- A C++20 compiler (MSVC 19.3x+, GCC 12+, Clang 15+)
- Network access on the **first** configure

GoogleTest is fetched at configure time by `FetchContent` rather than vendored,
so nothing binary lives in the repository. Once `build/<preset>/_deps` is
populated the build works offline. To skip the test suite entirely — and with
it the download — configure with `-DPQ_ENGINE_BUILD_TESTS=OFF`.

## Conventions

Formatting is settled by `.clang-format`, not by review:

```bash
clang-format -i $(git ls-files 'cpp-engine/**/*.cpp' 'cpp-engine/**/*.hpp')
```

The `ci` preset compiles with warnings as errors and a broad warning set
(`-Wconversion`, `-Wshadow`, `-Wold-style-cast`, `/W4 /permissive-`). Code that
will eventually run on a latency-sensitive path should not be accumulating
implicit conversions.

## What goes here later

Phase 16 — C++ Performance. Candidates are the components where managed
allocation and GC pauses are the constraint: order book maintenance, market
data decoding, and the event bus. Everything else stays in .NET or Python,
where it is faster to write and easier to change.
