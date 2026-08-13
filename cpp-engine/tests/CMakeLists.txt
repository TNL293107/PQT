include(FetchContent)

# GoogleTest is fetched at configure time rather than vendored, so the version
# is declared here and nothing binary lives in the repository.
#
# This means the first configure needs network access. CI caches the build
# directory; see docs/development/local-setup.md for the offline note.
FetchContent_Declare(
    googletest
    GIT_REPOSITORY https://github.com/google/googletest.git
    GIT_TAG v1.17.0
    GIT_SHALLOW TRUE
)

# Keep GoogleTest linking against the same runtime as the rest of the build
# on MSVC, which otherwise defaults to a static CRT and fails to link.
set(gtest_force_shared_crt ON CACHE BOOL "" FORCE)

FetchContent_MakeAvailable(googletest)

add_executable(engine_info_test engine_info_test.cpp)

target_link_libraries(engine_info_test
    PRIVATE
        pq::engine
        pq_engine_warnings
        GTest::gtest_main
)

include(GoogleTest)
gtest_discover_tests(engine_info_test)

# The CLI must actually run and identify itself. This catches a library that
# compiles and links but fails at start-up, which the unit tests cannot see.
#
# $<TARGET_FILE:...> rather than the bare target name: multi-config generators
# (Visual Studio, Xcode) put the binary under a per-configuration directory
# that a plain target name cannot resolve at test time.
add_test(NAME engine_cli_runs COMMAND "$<TARGET_FILE:pq_engine_cli>")
set_tests_properties(engine_cli_runs PROPERTIES
    PASS_REGULAR_EXPRESSION "pq-engine [0-9]+\\.[0-9]+\\.[0-9]+"
)
