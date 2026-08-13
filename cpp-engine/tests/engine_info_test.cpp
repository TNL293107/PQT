#include "pq/engine_info.hpp"

#include <string>
#include <string_view>

#include <gtest/gtest.h>

namespace {

// Smoke tests for the engine build. They assert the things a broken toolchain
// gets wrong: a missing version string, a compiler silently falling back to an
// older standard, or a library that does not link.

TEST(EngineInfoTest, VersionIsPopulatedFromCMake) {
    EXPECT_FALSE(pq::EngineInfo::version().empty());
}

TEST(EngineInfoTest, VersionLooksLikeASemanticVersion) {
    const std::string_view version = pq::EngineInfo::version();

    EXPECT_NE(version.find('.'), std::string_view::npos);
}

TEST(EngineInfoTest, CompiledAgainstCpp20OrLater) {
    // The engine relies on C++20. A build that quietly selected an older
    // standard would compile today and break on the first <bit> or concepts
    // use, far from the cause.
    EXPECT_GE(pq::EngineInfo::standard(), 202002L);
}

TEST(EngineInfoTest, CompilerIsIdentified) {
    EXPECT_NE(pq::EngineInfo::compiler(), "unknown");
}

TEST(EngineInfoTest, DescriptionNamesTheEngineAndItsVersion) {
    const std::string description = pq::EngineInfo::describe();

    EXPECT_NE(description.find("pq-engine"), std::string::npos);
    EXPECT_NE(description.find(std::string(pq::EngineInfo::version())),
              std::string::npos);
}

}  // namespace
