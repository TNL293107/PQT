cmake_minimum_required(VERSION 3.24)

project(pq_engine
    VERSION 0.1.0
    DESCRIPTION "Performance-sensitive components for the Personal Quant Terminal"
    LANGUAGES CXX
)

# ---------------------------------------------------------------------------
# Standard
#
# C++20 is the floor: the components this project will eventually hold
# (order book, market data decoding, event bus) rely on <span>, <bit>,
# designated initialisers and concepts.
# ---------------------------------------------------------------------------
set(CMAKE_CXX_STANDARD 20)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_CXX_EXTENSIONS OFF)

# Emitted for clangd / IDE tooling.
set(CMAKE_EXPORT_COMPILE_COMMANDS ON)

# Default to a build type when the generator is single-configuration and the
# caller did not choose one, so an accidental unoptimised build is obvious.
if(NOT CMAKE_BUILD_TYPE AND NOT CMAKE_CONFIGURATION_TYPES)
    set(CMAKE_BUILD_TYPE "Debug" CACHE STRING "Build type" FORCE)
endif()

# ---------------------------------------------------------------------------
# Warnings
#
# Applied through an interface target so every target opts in the same way.
# ---------------------------------------------------------------------------
add_library(pq_engine_warnings INTERFACE)

if(MSVC)
    target_compile_options(pq_engine_warnings INTERFACE
        /W4
        /permissive-      # standards conformance
        /w14640           # thread-unsafe static member initialisation
    )
else()
    target_compile_options(pq_engine_warnings INTERFACE
        -Wall
        -Wextra
        -Wpedantic
        -Wshadow
        -Wconversion
        -Wsign-conversion
        -Wnon-virtual-dtor
        -Wold-style-cast
    )
endif()

option(PQ_ENGINE_WARNINGS_AS_ERRORS "Treat compiler warnings as errors" OFF)
if(PQ_ENGINE_WARNINGS_AS_ERRORS)
    if(MSVC)
        target_compile_options(pq_engine_warnings INTERFACE /WX)
    else()
        target_compile_options(pq_engine_warnings INTERFACE -Werror)
    endif()
endif()

add_subdirectory(src)
add_subdirectory(app)

# ---------------------------------------------------------------------------
# Tests
# ---------------------------------------------------------------------------
option(PQ_ENGINE_BUILD_TESTS "Build the engine test suite" ON)

if(PQ_ENGINE_BUILD_TESTS)
    enable_testing()
    add_subdirectory(tests)
endif()
