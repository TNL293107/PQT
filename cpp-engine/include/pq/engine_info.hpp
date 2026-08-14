#pragma once

#include <string>
#include <string_view>

/// Performance-sensitive components of the Personal Quant Terminal.
///
/// Phase 0 contains no trading logic. This namespace exists so the C++
/// toolchain, the CMake build and CTest are proven end to end before any
/// latency-sensitive component is written. The order book, market data
/// decoding and the event bus arrive in Phase 16.
namespace pq {

/// Build and toolchain identification for the engine.
///
/// Reported by the engine binary and asserted by the smoke tests, so a
/// mis-wired build (wrong standard, wrong compiler, stale version) fails
/// visibly rather than producing a binary nobody can identify.
struct EngineInfo {
    /// Semantic version, injected by CMake from the project version.
    [[nodiscard]] static std::string_view version() noexcept;

    /// The C++ standard the translation unit was compiled against.
    [[nodiscard]] static long standard() noexcept;

    /// Compiler name and version, as far as the preprocessor can tell.
    ///
    /// Not `noexcept`: it builds a `std::string`, which allocates.
    [[nodiscard]] static std::string compiler();

    /// One-line summary suitable for a start-up log.
    [[nodiscard]] static std::string describe();
};

}  // namespace pq
