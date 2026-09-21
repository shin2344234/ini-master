// inimaster.h: optional helpers for ASI plugins that want to work well with
// INI Master. Single header, no dependencies beyond Windows.h. MIT licensed.
//
// Two things live here:
//
//   INIMASTER_EMBED_META(text)
//       Puts INI Master metadata (JSON or an annotated ini) inside the plugin,
//       for builds without a .rc file. With a .rc file, prefer this line in it:
//           INIMETA INIMETA "MyMod.inimeta"
//       INI Master reads either one from the file's bytes and never loads the
//       plugin. Use the macro in exactly one .cpp file.
//
//   inimaster::IniWatcher
//       Tells you when the ini has been rewritten, so the plugin can reread it
//       while the game runs. Declare "live": true in the metadata once the
//       plugin does this, and INI Master tells players their edits apply at once.
#pragma once

#ifndef WIN32_LEAN_AND_MEAN
#define WIN32_LEAN_AND_MEAN
#endif
#include <Windows.h>
#include <string>

#define INIMASTER_META_BEGIN "@@INIMETA@@"
#define INIMASTER_META_END "@@/INIMETA@@"

// MSVC caps one string literal piece at about 16 KB. Split longer text into
// several adjacent literals; they concatenate up to 64 KB. Past that, use the
// .rc route, which has no limit.
#if defined(_MSC_VER)
#pragma section(".inimeta", read)
#if defined(_M_IX86)
#define INIMASTER_KEEP_SYMBOL "/INCLUDE:_inimaster_meta_blob"
#else
#define INIMASTER_KEEP_SYMBOL "/INCLUDE:inimaster_meta_blob"
#endif
#define INIMASTER_EMBED_META(text)                                                              \
    extern "C" __declspec(allocate(".inimeta")) const char inimaster_meta_blob[] =              \
        INIMASTER_META_BEGIN text INIMASTER_META_END;                                           \
    __pragma(comment(linker, INIMASTER_KEEP_SYMBOL))
#else
#define INIMASTER_EMBED_META(text)                                                              \
    extern "C" __attribute__((used, section(".inimeta"))) const char inimaster_meta_blob[] =    \
        INIMASTER_META_BEGIN text INIMASTER_META_END;
#endif

namespace inimaster
{
    // Polls the ini's last-write time and size. One GetFileAttributesExW per
    // call, so calling it from a worker loop a few times a second costs
    // nothing. INI Master writes a new file and renames it into place, so a
    // change is always a whole, finished file.
    //
    //     static inimaster::IniWatcher watch(iniPath);
    //     if (watch.Changed()) ReadSettings();
    class IniWatcher
    {
    public:
        IniWatcher() = default;
        explicit IniWatcher(std::wstring path) : path_(std::move(path)) { stamp_ = Stamp(); }

        void Reset(std::wstring path)
        {
            path_ = std::move(path);
            stamp_ = Stamp();
        }

        // True once for every change since the last call.
        bool Changed()
        {
            const unsigned long long s = Stamp();
            if (s == stamp_) return false;
            stamp_ = s;
            return true;
        }

        const std::wstring& Path() const { return path_; }

    private:
        unsigned long long Stamp() const
        {
            WIN32_FILE_ATTRIBUTE_DATA fad{};
            if (path_.empty() || !GetFileAttributesExW(path_.c_str(), GetFileExInfoStandard, &fad)) return 0;
            const unsigned long long t = (static_cast<unsigned long long>(fad.ftLastWriteTime.dwHighDateTime) << 32) |
                                         fad.ftLastWriteTime.dwLowDateTime;
            return t ^ (static_cast<unsigned long long>(fad.nFileSizeLow) << 1);
        }

        std::wstring path_;
        unsigned long long stamp_ = 0;
    };

    // The full path of a file next to the module that contains this code,
    // usually "<bin64>\MyMod.ini".
    inline std::wstring BesideThisModule(const wchar_t* fileName)
    {
        HMODULE self = nullptr;
        GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,
                           reinterpret_cast<LPCWSTR>(&BesideThisModule), &self);
        wchar_t buf[MAX_PATH * 2]{};
        const DWORD n = GetModuleFileNameW(self, buf, static_cast<DWORD>(sizeof buf / sizeof buf[0]));
        std::wstring dir(buf, n);
        const size_t slash = dir.find_last_of(L"\\/");
        dir.resize(slash == std::wstring::npos ? 0 : slash + 1);
        return dir + fileName;
    }
}
