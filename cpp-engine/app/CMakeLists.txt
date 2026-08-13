add_executable(pq_engine_cli main.cpp)

set_target_properties(pq_engine_cli PROPERTIES OUTPUT_NAME "pq-engine")

target_link_libraries(pq_engine_cli
    PRIVATE
        pq::engine
        pq_engine_warnings
)
