# Generate the version header from the project version so the number is
# declared exactly once, in the top-level CMakeLists.txt.
configure_file(
    "${CMAKE_CURRENT_SOURCE_DIR}/engine_version.hpp.in"
    "${CMAKE_CURRENT_BINARY_DIR}/generated/pq/engine_version.hpp"
    @ONLY
)

add_library(pq_engine STATIC
    engine_info.cpp
)

add_library(pq::engine ALIAS pq_engine)

target_include_directories(pq_engine
    PUBLIC
        "$<BUILD_INTERFACE:${PROJECT_SOURCE_DIR}/include>"
    PRIVATE
        # The generated version header is an implementation detail; consumers
        # read the version through EngineInfo::version() instead.
        "${CMAKE_CURRENT_BINARY_DIR}/generated"
)

target_link_libraries(pq_engine PRIVATE pq_engine_warnings)
