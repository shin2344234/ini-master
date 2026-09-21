// A plugin that does nothing but show both ways of carrying INI Master
// metadata, and rereads its ini while the game runs.
//
// ExampleMod.rc embeds ExampleMod.inimeta as an INIMETA resource. The macro
// below embeds a second, smaller block through the marker route; a real
// plugin needs only one of the two. Where both describe the same key, the
// marker block wins over the resource.
#include "../inimaster.h"

#include <atomic>
#include <thread>

INIMASTER_EMBED_META(R"json({
  "ini": "ExampleMod.ini",
  "sections": {
    "settings": {
      "keys": {
        "Speed": { "unit": "x", "step": 0.25 }
      }
    }
  }
})json")

namespace
{
    std::atomic<bool> g_run{ true };
    std::atomic<int> g_speedHundredths{ 100 };

    void ReadSettings(const std::wstring& ini)
    {
        wchar_t buf[64]{};
        GetPrivateProfileStringW(L"settings", L"Speed", L"1.0", buf, 64, ini.c_str());
        g_speedHundredths = static_cast<int>(_wtof(buf) * 100.0 + 0.5);
    }

    void Worker()
    {
        const std::wstring ini = inimaster::BesideThisModule(L"ExampleMod.ini");
        inimaster::IniWatcher watch(ini);
        ReadSettings(ini);
        while (g_run)
        {
            if (watch.Changed()) ReadSettings(ini);
            Sleep(250);
        }
    }
}

BOOL APIENTRY DllMain(HMODULE module, DWORD reason, LPVOID)
{
    if (reason == DLL_PROCESS_ATTACH)
    {
        DisableThreadLibraryCalls(module);
        std::thread(Worker).detach();
    }
    else if (reason == DLL_PROCESS_DETACH)
    {
        g_run = false;
    }
    return TRUE;
}
