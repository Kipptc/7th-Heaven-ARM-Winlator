// DEFINE ----------------------------------------
#define WIN32_LEAN_AND_MEAN

#define MAIN_ASM_NAME L"AppProxy"
#define MAIN_TYP_NAME L"AppProxy.Proxy"
#define MAIN_FUN_NAME L"Main"

// PRAGMA ----------------------------------------

#pragma comment(linker, "/export:DirectInputCreateA=C:\\Windows\\System32\\dinput.DirectInputCreateA,@1")

// INCLUDE ---------------------------------------

#include <iostream>
#include <fstream>
#include <string>
#include <mutex>
#include <unordered_set>
#include <nlohmann/json.hpp>
using json = nlohmann::json;

#include <Windows.h>
#include <stdio.h>
#include <detours/detours.h>
#include <nethost.h>
#include <hostfxr.h>
#include <coreclr_delegates.h>
#include <TlHelp32.h>
#include <StackWalker.h>
#include <plog/Log.h>
#include <plog/Initializers/RollingFileInitializer.h>
#include "plog.formatter.h"

#define X(n) n##_fn n;
#include "hostfxr.x.h"
#include "delegates.x.h"
#undef X

// UTILS

// trim from start (in place)
static inline void ltrim(std::string& s) {
    s.erase(s.begin(), std::find_if(s.begin(), s.end(), [](unsigned char ch) {
        return !std::isspace(ch);
        }));
}

// trim from end (in place)
static inline void rtrim(std::string& s) {
    s.erase(std::find_if(s.rbegin(), s.rend(), [](unsigned char ch) {
        return !std::isspace(ch);
        }).base(), s.end());
}

// trim from both ends (in place)
static inline void trim(std::string& s) {
    rtrim(s);
    ltrim(s);
}

// EXPORTS ---------------------------------------

struct host_exports
{
    void (*Shutdown)();
#define X(n) decltype(&n) n;
#include "host_exports.x.h"
#undef X
} exports;

// IMPORTS ---------------------------------------

// API to initialize AppProxy
static HRESULT(WINAPI* HostInitialize)(host_exports*) = nullptr;

// CreateFileW
static HANDLE(WINAPI* TrueCreateFileW)(LPCWSTR lpFileName, DWORD dwDesiredAccess, DWORD dwShareMode, LPSECURITY_ATTRIBUTES lpSecurityAttributes, DWORD dwCreationDisposition, DWORD dwFlagsAndAttributes, HANDLE hTemplateFile) = CreateFileW;

// ReadFile
static BOOL(WINAPI* TrueReadFile)(HANDLE hFile, LPVOID lpBuffer, DWORD nNumberOfBytesToRead, LPDWORD lpNumberOfBytesRead, LPOVERLAPPED lpOverlapped) = ReadFile;

// FindFirstFileW
static HANDLE(WINAPI* TrueFindFirstFileW)(LPCWSTR lpFileName, LPWIN32_FIND_DATAW lpFindFileData) = FindFirstFileW;

// FindFirstFileExW
static HANDLE(WINAPI* TrueFindFirstFileExW)(LPCWSTR lpFileName, FINDEX_INFO_LEVELS fInfoLevelId, LPVOID lpFindFileData, FINDEX_SEARCH_OPS fSearchOp, LPVOID lpSearchFilter, DWORD dwAdditionalFlags) = FindFirstFileExW;

// FindNextFileW
static BOOL(WINAPI* TrueFindNextFileW)(HANDLE hFindFile, LPWIN32_FIND_DATAW lpFindFileData) = FindNextFileW;

// FindClose
static BOOL(WINAPI* TrueFindClose)(HANDLE hFindFile) = FindClose;

// SetFilePointer
static DWORD(WINAPI* TrueSetFilePointer)(HANDLE hFile, LONG lDistanceToMove, PLONG lpDistanceToMoveHigh, DWORD dwMoveMethod) = SetFilePointer;

// SetFilePointerEx
static BOOL(WINAPI* TrueSetFilePointerEx)(HANDLE hFile, LARGE_INTEGER liDistanceToMove, PLARGE_INTEGER lpNewFilePointer, DWORD dwMoveMethod) = SetFilePointerEx;

// CloseHandle
static BOOL(WINAPI* TrueCloseHandle)(HANDLE hObject) = CloseHandle;

// GetFileType
static DWORD(WINAPI* TrueGetFileType)(HANDLE hFile) = GetFileType;

// GetFileInformationByHandle
static BOOL(WINAPI* TrueGetFileInformationByHandle)(HANDLE hFile, LPBY_HANDLE_FILE_INFORMATION lpFileInformation) = GetFileInformationByHandle;

// DuplicateHandle
static BOOL(WINAPI* TrueDuplicateHandle)(HANDLE hSourceProcessHandle, HANDLE hSourceHandle, HANDLE hTargetProcessHandle, LPHANDLE lpTargetHandle, DWORD dwDesiredAccess, BOOL bInheritHandle, DWORD dwOptions) = DuplicateHandle;

// GetFileSize
static DWORD(WINAPI* TrueGetFileSize)(HANDLE hFile, LPDWORD lpFileSizeHigh) = GetFileSize;

// GetFileSizeEx
static BOOL(WINAPI* TrueGetFileSizeEx)(HANDLE hFile, PLARGE_INTEGER lpFileSize) = GetFileSizeEx;

// GetFileAttributesExW
static BOOL(WINAPI* TrueGetFileAttributesExW)(LPCWSTR lpFileName, GET_FILEEX_INFO_LEVELS fInfoLevelId, LPVOID lpFileInformation) = GetFileAttributesExW;

// PostQuitMessage
static VOID(WINAPI* TruePostQuitMessage)(int nExitCode) = PostQuitMessage;

// Game WinMain
static int(WINAPI* GameWinMain)(HINSTANCE hInstance, HINSTANCE hPrevInstance, LPSTR lpCmdLine, int nShowCmd) = (int(WINAPI*)(HINSTANCE, HINSTANCE, LPSTR, int))0x67DB30;

// VARS ------------------------------------------

DWORD currentMainThreadId = 0;
HANDLE currentMainThread = nullptr;
thread_local bool inDotNetCode = false;
static std::mutex virtualHandlesMutex;
static std::unordered_set<HANDLE> virtualHandles;

static bool IsVirtualHandle(HANDLE handle)
{
    std::lock_guard<std::mutex> lock(virtualHandlesMutex);
    return virtualHandles.contains(handle);
}

static void RegisterVirtualHandle(HANDLE handle)
{
    std::lock_guard<std::mutex> lock(virtualHandlesMutex);
    virtualHandles.insert(handle);
}

static bool UnregisterVirtualHandle(HANDLE handle)
{
    std::lock_guard<std::mutex> lock(virtualHandlesMutex);
    return virtualHandles.erase(handle) != 0;
}

// FUNCTIONS -------------------------------------

HANDLE WINAPI _CreateFileW(LPCWSTR lpFileName, DWORD dwDesiredAccess, DWORD dwShareMode, LPSECURITY_ATTRIBUTES lpSecurityAttributes, DWORD dwCreationDisposition, DWORD dwFlagsAndAttributes, HANDLE hTemplateFile)
{
    HANDLE ret = nullptr;

    if (exports.CreateFileW)
    {
        if (!inDotNetCode)
        {
            inDotNetCode = true;
            ret = exports.CreateFileW(lpFileName, dwDesiredAccess, dwShareMode, lpSecurityAttributes, dwCreationDisposition, dwFlagsAndAttributes, hTemplateFile);
            inDotNetCode = false;
        }
    }

    if (ret != nullptr)
        RegisterVirtualHandle(ret);
    else
        ret = TrueCreateFileW(lpFileName, dwDesiredAccess, dwShareMode, lpSecurityAttributes, dwCreationDisposition, dwFlagsAndAttributes, hTemplateFile);

    return ret;
}

BOOL WINAPI _ReadFile(HANDLE hFile, LPVOID lpBuffer, DWORD nNumberOfBytesToRead, LPDWORD lpNumberOfBytesRead, LPOVERLAPPED lpOverlapped)
{
    BOOL ret = FALSE;

    if (exports.ReadFile && !inDotNetCode && IsVirtualHandle(hFile))
    {
        inDotNetCode = true;
        ret = exports.ReadFile(hFile, lpBuffer, nNumberOfBytesToRead, lpNumberOfBytesRead, lpOverlapped);
        inDotNetCode = false;
    }

    if (ret == FALSE)
        ret = TrueReadFile(hFile, lpBuffer, nNumberOfBytesToRead, lpNumberOfBytesRead, lpOverlapped);

    return ret;
}

HANDLE WINAPI _FindFirstFileW(LPCWSTR lpFileName, LPWIN32_FIND_DATAW lpFindFileData)
{
    HANDLE ret = nullptr;

    if (exports.FindFirstFileW && !inDotNetCode)
    {
        inDotNetCode = true;
        ret = exports.FindFirstFileW(lpFileName, lpFindFileData);
        inDotNetCode = false;
    }

    if (ret == nullptr)
        ret = TrueFindFirstFileW(lpFileName, lpFindFileData);

    return ret;
}

HANDLE WINAPI _FindFirstFileExW(LPCWSTR lpFileName, FINDEX_INFO_LEVELS fInfoLevelId, LPVOID lpFindFileData, FINDEX_SEARCH_OPS fSearchOp, LPVOID lpSearchFilter, DWORD dwAdditionalFlags)
{
    HANDLE ret = nullptr;

    if (exports.FindFirstFileExW && !inDotNetCode)
    {
        inDotNetCode = true;
        ret = exports.FindFirstFileExW(lpFileName, fInfoLevelId, lpFindFileData, fSearchOp, lpSearchFilter, dwAdditionalFlags);
        inDotNetCode = false;
    }

    if (ret == nullptr)
        ret = TrueFindFirstFileExW(lpFileName, fInfoLevelId, lpFindFileData, fSearchOp, lpSearchFilter, dwAdditionalFlags);

    return ret;
}

BOOL WINAPI _FindNextFileW(HANDLE hFindFile, LPWIN32_FIND_DATAW lpFindFileData)
{
    BOOL ret = FALSE;

    if (exports.FindNextFileW && !inDotNetCode)
    {
        inDotNetCode = true;
        ret = exports.FindNextFileW(hFindFile, lpFindFileData);
        inDotNetCode = false;
    }

    if (ret == FALSE)
        ret = TrueFindNextFileW(hFindFile, lpFindFileData);
    else if (ret < FALSE)
        ret = FALSE;

    return ret;
}

BOOL WINAPI _FindClose(HANDLE hFindFile)
{
    BOOL ret = FALSE;

    if (exports.FindClose && !inDotNetCode)
    {
        inDotNetCode = true;
        ret = exports.FindClose(hFindFile);
        inDotNetCode = false;
    }

    if (ret == FALSE)
        ret = TrueFindClose(hFindFile);

    return ret;
}

DWORD WINAPI _SetFilePointer(HANDLE hFile, LONG lDistanceToMove, PLONG lpDistanceToMoveHigh, DWORD dwMoveMethod)
{
    DWORD ret = INVALID_SET_FILE_POINTER;

    if (exports.SetFilePointer && !inDotNetCode && IsVirtualHandle(hFile))
    {
        inDotNetCode = true;
        ret = exports.SetFilePointer(hFile, lDistanceToMove, lpDistanceToMoveHigh, dwMoveMethod);
        inDotNetCode = false;
    }

    if (ret == INVALID_SET_FILE_POINTER)
        ret = TrueSetFilePointer(hFile, lDistanceToMove, lpDistanceToMoveHigh, dwMoveMethod);

    return ret;
}

BOOL WINAPI _SetFilePointerEx(HANDLE hFile, LARGE_INTEGER liDistanceToMove, PLARGE_INTEGER lpNewFilePointer, DWORD dwMoveMethod)
{
    BOOL ret = FALSE;

    if (exports.SetFilePointerEx && !inDotNetCode && IsVirtualHandle(hFile))
    {
        inDotNetCode = true;
        ret = exports.SetFilePointerEx(hFile, liDistanceToMove, lpNewFilePointer, dwMoveMethod);
        inDotNetCode = false;
    }

    if (ret == FALSE)
        ret = TrueSetFilePointerEx(hFile, liDistanceToMove, lpNewFilePointer, dwMoveMethod);

    return ret;
}

BOOL WINAPI _CloseHandle(HANDLE hObject)
{
    bool isVirtual = IsVirtualHandle(hObject);
    if (exports.CloseHandle && !inDotNetCode && isVirtual)
    {
        inDotNetCode = true;
        exports.CloseHandle(hObject);
        inDotNetCode = false;
    }

    if (isVirtual)
        UnregisterVirtualHandle(hObject);

    return TrueCloseHandle(hObject);
}

DWORD WINAPI _GetFileType(HANDLE hFile)
{
    // Preserve the wrapper's disk-file classification for redirected handles
    // without entering .NET or its reflection invocation stub.
    if (IsVirtualHandle(hFile)) return FILE_TYPE_DISK;
    return TrueGetFileType(hFile);
}

BOOL WINAPI _GetFileInformationByHandle(HANDLE hFile, LPBY_HANDLE_FILE_INFORMATION lpFileInformation)
{
    BOOL ret = FALSE;

    if (exports.GetFileInformationByHandle && IsVirtualHandle(hFile))
    {
        if (!inDotNetCode)
        {
            inDotNetCode = true;
            ret = exports.GetFileInformationByHandle(hFile, lpFileInformation);
            inDotNetCode = false;
        }
    }

    if (ret == FALSE)
        ret = TrueGetFileInformationByHandle(hFile, lpFileInformation);

    return ret;
}

BOOL WINAPI _DuplicateHandle(HANDLE hSourceProcessHandle, HANDLE hSourceHandle, HANDLE hTargetProcessHandle, LPHANDLE lpTargetHandle, DWORD dwDesiredAccess, BOOL bInheritHandle, DWORD dwOptions)
{
    BOOL ret = TrueDuplicateHandle(hSourceProcessHandle, hSourceHandle, hTargetProcessHandle, lpTargetHandle, dwDesiredAccess, bInheritHandle, dwOptions);

    if (exports.DuplicateHandle && !inDotNetCode && ret && IsVirtualHandle(hSourceHandle))
    {
        RegisterVirtualHandle(*lpTargetHandle);
        inDotNetCode = true;
        exports.DuplicateHandle(hSourceProcessHandle, hSourceHandle, hTargetProcessHandle, lpTargetHandle, dwDesiredAccess, bInheritHandle, dwOptions);
        inDotNetCode = false;
    }

    return ret;
}

DWORD WINAPI _GetFileSize(HANDLE hFile, LPDWORD lpFileSizeHigh)
{
    DWORD ret = INVALID_FILE_SIZE;

    if (exports.GetFileSize && !inDotNetCode && IsVirtualHandle(hFile))
    {
        inDotNetCode = true;
        ret = exports.GetFileSize(hFile, lpFileSizeHigh);
        inDotNetCode = false;
    }

    if (ret == INVALID_FILE_SIZE)
        ret = TrueGetFileSize(hFile, lpFileSizeHigh);

    return ret;
}

BOOL WINAPI _GetFileSizeEx(HANDLE hFile, PLARGE_INTEGER lpFileSize)
{
    BOOL ret = FALSE;

    if (exports.GetFileSizeEx && !inDotNetCode && IsVirtualHandle(hFile))
    {
        inDotNetCode = true;
        ret = exports.GetFileSizeEx(hFile, lpFileSize);
        inDotNetCode = false;
    }

    if (ret == FALSE)
        ret = TrueGetFileSizeEx(hFile, lpFileSize);

    return ret;
}

BOOL WINAPI _GetFileAttributesExW(LPCWSTR lpFileName, GET_FILEEX_INFO_LEVELS fInfoLevelId, LPVOID lpFileInformation)
{
    BOOL ret = FALSE;

    if (exports.GetFileAttributesExW && !inDotNetCode)
    {
        inDotNetCode = true;
        ret = exports.GetFileAttributesExW(lpFileName, fInfoLevelId, lpFileInformation);
        inDotNetCode = false;
    }

    if (ret == FALSE)
        ret = TrueGetFileAttributesExW(lpFileName, fInfoLevelId, lpFileInformation);

    return ret;
}

VOID WINAPI _PostQuitMessage(int nExitCode)
{
    if (GetCurrentThreadId() == currentMainThreadId)
    {
        // Unhook Win32 APIs
        DetourTransactionBegin();
        DetourUpdateThread(GetCurrentThread());
        // ------------------------------------
        DetourDetach((PVOID*)&TrueCreateFileW, _CreateFileW);
        DetourDetach((PVOID*)&TrueReadFile, _ReadFile);
        DetourDetach((PVOID*)&TrueFindFirstFileW, _FindFirstFileW);
        DetourDetach((PVOID*)&TrueFindFirstFileExW, _FindFirstFileExW);
        DetourDetach((PVOID*)&TrueFindNextFileW, _FindNextFileW);
        DetourDetach((PVOID*)&TrueFindClose, _FindClose);
        DetourDetach((PVOID*)&TrueSetFilePointer, _SetFilePointer);
        DetourDetach((PVOID*)&TrueSetFilePointerEx, _SetFilePointerEx);
        DetourDetach((PVOID*)&TrueCloseHandle, _CloseHandle);
        DetourDetach((PVOID*)&TrueGetFileType, _GetFileType);
        DetourDetach((PVOID*)&TrueGetFileInformationByHandle, _GetFileInformationByHandle);
        DetourDetach((PVOID*)&TrueDuplicateHandle, _DuplicateHandle);
        DetourDetach((PVOID*)&TrueGetFileSize, _GetFileSize);
        DetourDetach((PVOID*)&TrueGetFileSizeEx, _GetFileSizeEx);
        DetourDetach((PVOID*)&TrueGetFileAttributesExW, _GetFileAttributesExW);
        DetourDetach((PVOID*)&TruePostQuitMessage, _PostQuitMessage);
        // ------------------------------------
        DetourTransactionCommit();

        // Ask the .NET code to gracefully shutdown
        if (exports.Shutdown) exports.Shutdown();
    }

    // Continue with the usual execution
    TruePostQuitMessage(nExitCode);
}

// MAIN ------------------------------------------

class _7thStackWalker : public StackWalker
{
public:
    _7thStackWalker(bool muted = false) : StackWalker(), _baseAddress(0), _size(0), _muted(muted) {}
    DWORD64 getBaseAddress() const {
        return _baseAddress;
    }
    DWORD getSize() const {
        return _size;
    }
protected:
    virtual void OnLoadModule(LPCSTR img, LPCSTR mod, DWORD64 baseAddr,
        DWORD size, DWORD result, LPCSTR symType, LPCSTR pdbName,
        ULONGLONG fileVersion
    )
    {
        if (_baseAddress == 0 && _size == 0)
        {
            _baseAddress = baseAddr;
            _size = size;
        }
        StackWalker::OnLoadModule(
            img, mod, baseAddr, size, result, symType, pdbName, fileVersion
        );
    }

    virtual void OnDbgHelpErr(LPCSTR szFuncName, DWORD gle, DWORD64 addr)
    {
        // Silence is golden.
    }

    virtual void OnOutput(LPCSTR szText)
    {
        if (!_muted)
        {
            std::string tmp(szText);
            trim(tmp);
            PLOGV << tmp;
        }
    }
private:
    DWORD64 _baseAddress;
    DWORD _size;
    bool _muted;
};

LONG WINAPI ExceptionHandler(EXCEPTION_POINTERS* ep)
{
    PLOGV << "*** Exception 0x" << std::hex << ep->ExceptionRecord->ExceptionCode << ", address 0x" << std::hex << ep->ExceptionRecord->ExceptionAddress << " ***";

    _7thStackWalker sw;
    sw.ShowCallstack(
        GetCurrentThread(),
        ep->ContextRecord
    );

    PLOGE << "Unhandled Exception. See dumped information above.";

    // This exception is mostly called for this reason, hint the user
    std::ifstream f(MAIN_ASM_NAME L".runtimeconfig.json");
    json data = json::parse(f);

    std::string version = data["runtimeOptions"]["framework"]["version"];
    std::string msg = "Could not start the .NET Desktop Runtime version " + version + ".\n\nPlease make sure you have both the x86 and x64 editions installed. Try using the 7th Heaven exe installer, or visit https://dotnet.microsoft.com for more information.";
    MessageBoxA(NULL, msg.c_str(), "Error", MB_OK | MB_ICONERROR);

    // let OS handle the crash
    SetUnhandledExceptionFilter(0);
    return EXCEPTION_CONTINUE_EXECUTION;
}

#ifndef MAKEULONGLONG
#define MAKEULONGLONG(ldw, hdw) ((ULONGLONG(hdw) << 32) | ((ldw) & 0xFFFFFFFF))
#endif

DWORD GetCurrentProcessMainThreadId()
{
    DWORD dwMainThreadID = 0;
    ULONGLONG ullMinCreateTime = MAXULONGLONG;

    HANDLE hThreadSnap = CreateToolhelp32Snapshot(TH32CS_SNAPTHREAD, 0);
    if (hThreadSnap != INVALID_HANDLE_VALUE) {
        THREADENTRY32 th32;
        th32.dwSize = sizeof(THREADENTRY32);
        BOOL bOK = TRUE;
        for (bOK = Thread32First(hThreadSnap, &th32); bOK; bOK = Thread32Next(hThreadSnap, &th32))
        {
            if (th32.th32OwnerProcessID == GetCurrentProcessId())
            {
                HANDLE hThread = OpenThread(THREAD_QUERY_INFORMATION, TRUE, th32.th32ThreadID);
                if (hThread)
                {
                    FILETIME afTimes[4] = { 0 };
                    if (GetThreadTimes(hThread, &afTimes[0], &afTimes[1], &afTimes[2], &afTimes[3]))
                    {
                        ULONGLONG ullTest = MAKEULONGLONG(afTimes[0].dwLowDateTime, afTimes[0].dwHighDateTime);
                        if (ullTest && ullTest < ullMinCreateTime)
                        {
                            ullMinCreateTime = ullTest;
                            dwMainThreadID = th32.th32ThreadID; // let it be main... :)
                        }
                    }
                    CloseHandle(hThread);
                }
            }
        }
#ifndef UNDER_CE
        CloseHandle(hThreadSnap);
#else
        CloseToolhelp32Snapshot(hThreadSnap);
#endif
    }

    return dwMainThreadID;
}

DWORD WINAPI StartProxy(LPVOID lpParam) {
    HINSTANCE hinstDLL = (HINSTANCE)lpParam;



    return 0;
}

BOOL WINAPI DllMain(HINSTANCE hinstDLL, DWORD fdwReason, LPVOID lpReserved)
{
    // Move on if the current process is an helper process or the reason is not attach
    if (fdwReason != DLL_PROCESS_ATTACH) return TRUE;
    if (DetourIsHelperProcess()) return TRUE;

    // Keep the loader log in Bannerlator Downloads when that drive is mounted.
    // A local file remains the fallback for ordinary Windows installations.
    const char* loader_log = "AppLoader.log";
    const DWORD download_drive = GetFileAttributesA("D:\\");
    if (download_drive != INVALID_FILE_ATTRIBUTES &&
        (download_drive & FILE_ATTRIBUTE_DIRECTORY) != 0 &&
        (CreateDirectoryA("D:\\7h-ARM", nullptr) || GetLastError() == ERROR_ALREADY_EXISTS) &&
        (CreateDirectoryA("D:\\7h-ARM\\logs", nullptr) || GetLastError() == ERROR_ALREADY_EXISTS))
        loader_log = "D:\\7h-ARM\\logs\\AppLoader.log";
    remove(loader_log);
    plog::init<plog::_7thFormatter>(plog::verbose, loader_log);
    PLOGI << "AppLoader init log";

    // Log unhandled exceptions
    SetUnhandledExceptionFilter(ExceptionHandler);

    // Save current main thread if for FF7.exe
    currentMainThreadId = GetCurrentProcessMainThreadId();

    // Get current process name
    CHAR parentName[1024];
    GetModuleFileNameA(NULL, parentName, sizeof(parentName));
    _strlwr(parentName);

    // Begin the detouring
    static auto target = GameWinMain;
    static decltype(target) detour = [](HINSTANCE hInstance, HINSTANCE hPrevInstance, LPSTR lpCmdLine, int nShowCmd) -> int
        {
            DetourTransactionBegin();
            DetourUpdateThread(GetCurrentThread());
            // ------------------------------------
            DetourDetach((void**)&target, detour);
            // ------------------------------------
            DetourTransactionCommit();

            wchar_t game_path[MAX_PATH] = {};
            const DWORD game_path_length = GetModuleFileNameW(nullptr, game_path, MAX_PATH);
            if (game_path_length == 0 || game_path_length >= MAX_PATH)
            {
                PLOGE << "Could not determine the FF7 executable directory.";
                return target(hInstance, hPrevInstance, lpCmdLine, nShowCmd);
            }
            std::wstring game_directory(game_path);
            const auto separator = game_directory.find_last_of(L"\\/");
            if (separator == std::wstring::npos)
            {
                PLOGE << "The FF7 executable path has no parent directory.";
                return target(hInstance, hPrevInstance, lpCmdLine, nShowCmd);
            }
            game_directory.resize(separator + 1);

            // CoreCLR's writable/executable JIT mode can fail under FEX on Wine.
            // Apply this only to the game process before hostfxr starts the runtime,
            // while respecting an explicit container or shortcut override.
            const HMODULE ntdll = GetModuleHandleW(L"ntdll.dll");
            if (ntdll != nullptr && GetProcAddress(ntdll, "wine_get_version") != nullptr)
            {
                wchar_t write_xor_execute[16] = {};
                if (GetEnvironmentVariableW(L"DOTNET_EnableWriteXorExecute", write_xor_execute, 16) == 0)
                {
                    if (SetEnvironmentVariableW(L"DOTNET_EnableWriteXorExecute", L"0"))
                        PLOGI << "Set DOTNET_EnableWriteXorExecute=0 before starting the x86 .NET runtime.";
                    else
                        PLOGW << "Could not set DOTNET_EnableWriteXorExecute (Win32 error " << GetLastError() << ").";
                }
                else
                    PLOGI << "Using the existing DOTNET_EnableWriteXorExecute container setting.";
            }

            wchar_t private_dotnet_root[MAX_PATH] = {};
            DWORD root_length = GetEnvironmentVariableW(L"SEVENTH_HEAVEN_DOTNET_ROOT_X86", private_dotnet_root, MAX_PATH);
            if (root_length == 0 || root_length >= MAX_PATH)
            {
                const std::wstring runtime_config_path = game_directory + L"7thHeavenRuntime.ini";
                root_length = GetPrivateProfileStringW(L"Runtime", L"X86DotnetRoot", L"",
                    private_dotnet_root, MAX_PATH, runtime_config_path.c_str());
            }
            get_hostfxr_parameters hostfxr_parameters = { sizeof(get_hostfxr_parameters), nullptr,
                root_length > 0 && root_length < MAX_PATH ? private_dotnet_root : nullptr };

            size_t buffer_size = 0;
            get_hostfxr_path(nullptr, &buffer_size, &hostfxr_parameters);

            if (buffer_size == 0)
            {
                PLOGE << "Could not locate the x86 .NET hostfxr. AppProxy needs a matching x86 .NET runtime to load mods.";
                return target(hInstance, hPrevInstance, lpCmdLine, nShowCmd);
            }

            auto buffer = new char_t[buffer_size];
            const int hostfxr_path_result = get_hostfxr_path(buffer, &buffer_size, &hostfxr_parameters);

            if (hostfxr_path_result != 0)
            {
                PLOGE << "Could not resolve the x86 .NET hostfxr path (error " << hostfxr_path_result << ").";
                delete[] buffer;
                return target(hInstance, hPrevInstance, lpCmdLine, nShowCmd);
            }

            auto hostfxr = LoadLibraryW(buffer);
            delete[] buffer;

            if (hostfxr == nullptr)
            {
                PLOGE << "Could not load the x86 .NET hostfxr (Win32 error " << GetLastError() << ").";
                return target(hInstance, hPrevInstance, lpCmdLine, nShowCmd);
            }

#define X(n) *(void**)&n = GetProcAddress(hostfxr, #n);
#include "hostfxr.x.h"
#undef X

            hostfxr_handle context = nullptr;
            hostfxr_set_error_writer([](auto message) { OutputDebugString(message); });
            const std::wstring proxy_runtime_config = game_directory + MAIN_ASM_NAME L".runtimeconfig.json";
            const int init_result = hostfxr_initialize_for_runtime_config(proxy_runtime_config.c_str(), nullptr, &context);
            if (init_result != 0 || context == nullptr)
            {
                PLOGE << "Could not initialize AppProxy runtime (error " << init_result << ").";
                return target(hInstance, hPrevInstance, lpCmdLine, nShowCmd);
            }

#define X(n) hostfxr_get_runtime_delegate(context, hdt_##n, (void**)&n);
#include "delegates.x.h"
#undef X

            hostfxr_close(context);

            // Get main entry point and load the assembly
            const std::wstring proxy_assembly = game_directory + MAIN_ASM_NAME L".dll";
            const int load_result = load_assembly_and_get_function_pointer(proxy_assembly.c_str(), MAIN_TYP_NAME L", " MAIN_ASM_NAME, MAIN_FUN_NAME, UNMANAGEDCALLERSONLY_METHOD, nullptr, (void**)&HostInitialize);
            if (load_result != 0 || HostInitialize == nullptr)
            {
                PLOGE << "Could not load AppProxy entry point (error " << load_result << ").";
                return target(hInstance, hPrevInstance, lpCmdLine, nShowCmd);
            }

            // Start the AppProxy process
            HostInitialize(&exports);

            // Hook Win32 APIs
            DetourTransactionBegin();
            DetourUpdateThread(GetCurrentThread());
            // ------------------------------------
            DetourAttach((PVOID*)&TrueCreateFileW, _CreateFileW);
            DetourAttach((PVOID*)&TrueReadFile, _ReadFile);
            DetourAttach((PVOID*)&TrueFindFirstFileW, _FindFirstFileW);
            DetourAttach((PVOID*)&TrueFindFirstFileExW, _FindFirstFileExW);
            DetourAttach((PVOID*)&TrueFindNextFileW, _FindNextFileW);
            DetourAttach((PVOID*)&TrueFindClose, _FindClose);
            DetourAttach((PVOID*)&TrueSetFilePointer, _SetFilePointer);
            DetourAttach((PVOID*)&TrueSetFilePointerEx, _SetFilePointerEx);
            DetourAttach((PVOID*)&TrueCloseHandle, _CloseHandle);
            DetourAttach((PVOID*)&TrueGetFileType, _GetFileType);
            DetourAttach((PVOID*)&TrueGetFileInformationByHandle, _GetFileInformationByHandle);
            DetourAttach((PVOID*)&TrueDuplicateHandle, _DuplicateHandle);
            DetourAttach((PVOID*)&TrueGetFileSize, _GetFileSize);
            DetourAttach((PVOID*)&TrueGetFileSizeEx, _GetFileSizeEx);
            DetourAttach((PVOID*)&TrueGetFileAttributesExW, _GetFileAttributesExW);
            DetourAttach((PVOID*)&TruePostQuitMessage, _PostQuitMessage);
            // ------------------------------------
            DetourTransactionCommit();

            PLOGI << "AppLoader started successfully";

            return target(hInstance, hPrevInstance, lpCmdLine, nShowCmd);
        };

    DisableThreadLibraryCalls(hinstDLL);
    DetourTransactionBegin();
    DetourUpdateThread(GetCurrentThread());
    // ------------------------------------
    DetourAttach((void**)&target, detour);
    // ------------------------------------
    DetourTransactionCommit();

    return TRUE;
}
