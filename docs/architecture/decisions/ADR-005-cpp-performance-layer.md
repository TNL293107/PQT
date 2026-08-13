# ADR-005: C++ for the latency-sensitive path

**Status:** Accepted · **Date:** 2026-08-13 · **Phase:** 0

## Context

A small number of planned components are genuinely latency- and
allocation-sensitive:

- Order book maintenance under a high update rate.
- Market data decoding on the ingest path.
- The event bus distributing ticks to consumers.

These share a profile the rest of the system does not have: small operations,
executed at very high frequency, where garbage collection pauses and
allocation pressure dominate the cost.

Everything else in the system — HTTP handling, screening, backtesting,
reporting — is bound by I/O or by developer time, not by microseconds.

## Decision

A C++20 layer under `cpp-engine/`, built with CMake and tested with GoogleTest
through CTest.

Phase 0 builds a static library, a CLI and a passing test suite. It contains no
trading logic. The engine is not referenced by the backend and will not be
until Phase 15.

## Alternatives

**C# for everything.** Modern .NET has `Span<T>`, `ArrayPool`, struct generics
and server GC; a great deal of low-latency work is done in C# today.

**Rust**, for the same performance with memory safety.

**Defer the decision entirely** until Phase 15.

## Reasoning

The honest position is that .NET would probably be fast enough, and it must be
measured before any component is rewritten. This ADR does not claim otherwise.
What it establishes is the *option*, at close to zero cost, before Phase 15 is
under time pressure.

C++ over Rust is a deliberately pragmatic call rather than a technical
supremacy claim. Rust's safety guarantees are real and valuable. C++ was chosen
because the reference material in this specific domain — matching engines,
book implementations, exchange protocol decoders, and the vendor SDKs that
brokers actually ship — is overwhelmingly C++, and because P/Invoke interop
with .NET is a well-worn path. For a personal project whose purpose includes
learning trading-systems engineering, working in the language the field
actually uses is part of the value.

Deferring entirely was rejected for one reason: a toolchain is far easier to
stand up when nothing depends on it. Discovering a CMake, compiler, or CI
problem in Phase 15, while also designing an order book, means debugging two
unfamiliar things simultaneously. Proving the build now costs a day.

## Trade-offs

- A third language and toolchain, and the least memory-safe of the three.
- Interop is unresolved and will not be free.
- Real risk of premature optimisation: writing C++ for something that was never
  the bottleneck. Mitigated by the rule below.
- GoogleTest is fetched at configure time, so the first build needs network
  access.

## Consequences

- **Nothing moves to C++ without a measurement first.** A component is
  rewritten only after profiling shows the managed implementation is the
  bottleneck, and the rewrite must be benchmarked against what it replaces.
- The `ci` preset builds with warnings as errors and a wide warning set
  (`-Wconversion`, `-Wshadow`, `-Wold-style-cast`, `/W4 /permissive-`).
  Implicit conversions do not accumulate in code intended for a hot path.
- Formatting is settled by `.clang-format`, not by review.
- Tests run under CTest, which is also what GoogleTest registers with — so the
  harness does not change when the suite grows.
- **Open:** the interop mechanism (P/Invoke, native library, or separate
  process) is a Phase 15 decision.
