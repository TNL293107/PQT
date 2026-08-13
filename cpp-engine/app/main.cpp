#include <iostream>

#include "pq/engine_info.hpp"

/// Entry point for the engine binary.
///
/// Phase 0 does one thing: report what was built. It is the manual counterpart
/// to the CTest smoke suite — running it proves the library links and executes.
int main() {
    std::cout << pq::EngineInfo::describe() << '\n';
    std::cout << "No trading, market data or execution component is implemented.\n";
    return 0;
}
