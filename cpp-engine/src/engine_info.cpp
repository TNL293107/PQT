#include "pq/engine_info.hpp"

#include <sstream>
#include <string>
#include <string_view>

#include "pq/engine_version.hpp"

namespace pq {

std::string_view EngineInfo::version() noexcept {
    return PQ_ENGINE_VERSION;
}

long EngineInfo::standard() noexcept {
    // MSVC reports 199711L for __cplusplus unless /Zc:__cplusplus is set, and
    // exposes the real value through _MSVC_LANG instead.
#if defined(_MSVC_LANG)
    return static_cast<long>(_MSVC_LANG);
#else
    return static_cast<long>(__cplusplus);
#endif
}

std::string EngineInfo::compiler() {
    std::ostringstream out;

#if defined(__clang__)
    out << "clang " << __clang_major__ << '.' << __clang_minor__ << '.'
        << __clang_patchlevel__;
#elif defined(__GNUC__)
    out << "gcc " << __GNUC__ << '.' << __GNUC_MINOR__ << '.'
        << __GNUC_PATCHLEVEL__;
#elif defined(_MSC_VER)
    out << "msvc " << _MSC_VER;
#else
    out << "unknown";
#endif

    return out.str();
}

std::string EngineInfo::describe() {
    std::ostringstream out;
    out << "pq-engine " << version() << " (C++" << standard() << ", " << compiler()
        << ')';
    return out.str();
}

}  // namespace pq
