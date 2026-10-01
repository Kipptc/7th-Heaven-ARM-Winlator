using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;

namespace AppProxy
{
    static unsafe class Proxy
    {
        private static Assembly? lib = null;
        private static Type? t = null;

        private static MethodInfo? _mRun = null;
        private static MethodInfo? _mShutdown = null;
        private static MethodInfo? _mHCreateFileW = null;
        private static MethodInfo? _mHReadFile = null;
        private static MethodInfo? _mHFindFirstFileW = null;
        private static MethodInfo? _mHFindFirstFileExW = null;
        private static MethodInfo? _mHFindNextFileW = null;
        private static MethodInfo? _mHFindClose = null;
        private static MethodInfo? _mHSetFilePointer = null;
        private static MethodInfo? _mHSetFilePointerEx = null;
        private static MethodInfo? _mHCloseHandle = null;
        private static MethodInfo? _mHGetFileType = null;
        private static MethodInfo? _mHGetFileInformationByHandle = null;
        private static MethodInfo? _mHDuplicateHandle = null;
        private static MethodInfo? _mHGetFileSize = null;
        private static MethodInfo? _mHGetFileSizeEx = null;
        private static MethodInfo? _mHGetFileAttributesExW = null;

        private delegate IntPtr CreateFileWHandler(string path, FileAccess access, FileShare share, IntPtr securityAttributes, FileMode disposition, FileAttributes attributes, IntPtr templateFile);
        private delegate int ReadFileHandler(IntPtr handle, IntPtr bytes, uint bytesToRead, IntPtr bytesRead, IntPtr overlapped);
        private delegate int SetFilePointerExHandler(IntPtr handle, long distance, IntPtr newPointer, uint moveMethod);
        private static CreateFileWHandler? _createFileW;
        private static ReadFileHandler? _readFile;
        private static SetFilePointerExHandler? _setFilePointerEx;

        [StructLayout(LayoutKind.Sequential)]
        public struct HostExports
        {
            public delegate* unmanaged<void> Shutdown;
            public delegate* unmanaged<ushort*, uint, uint, void*, uint, uint, void*, void*> CreateFileW;
            public delegate* unmanaged<void*, void*, uint, uint*, void*, int> ReadFile;
            //public delegate* unmanaged<void*, void*, uint, uint*, void*, int> WriteFile;
            public delegate* unmanaged<ushort*, void*, void*> FindFirstFileW;
            public delegate* unmanaged<ushort*, uint, void*, uint, void*, uint, void*> FindFirstFileExW;
            public delegate* unmanaged<void*, void*, int> FindNextFileW;
            public delegate* unmanaged<void*, int> FindClose;
            public delegate* unmanaged<void*, int, int*, uint, uint> SetFilePointer;
            public delegate* unmanaged<void*, long, void*, uint, int> SetFilePointerEx;
            public delegate* unmanaged<void*, int> CloseHandle;
            public delegate* unmanaged<void*, uint> GetFileType;
            public delegate* unmanaged<void*, void*, int> GetFileInformationByHandle;
            public delegate* unmanaged<void*, void*, void*, void**, uint, int, uint, int> DuplicateHandle;
            public delegate* unmanaged<void*, uint*, uint> GetFileSize;
            public delegate* unmanaged<void*, int*, int> GetFileSizeEx;
            public delegate* unmanaged<ushort*, uint, void*, int> GetFileAttributesExW;
        }

        private static HostExports* _exports;

        [UnmanagedCallersOnly]
        public static int Main(void* exports)
        {
            try
            {
                _exports = (HostExports*)exports;

                _exports->Shutdown = &Shutdown;
                _exports->CreateFileW = &HCreateFileW;
                _exports->ReadFile = &HReadFile;
                _exports->FindFirstFileW = &HFindFirstFileW;
                _exports->FindFirstFileExW = &HFindFirstFileExW;
                _exports->FindNextFileW = &HFindNextFileW;
                _exports->FindClose = &HFindClose;
                _exports->SetFilePointer = &HSetFilePointer;
                _exports->SetFilePointerEx = &HSetFilePointerEx;
                _exports->CloseHandle = &HCloseHandle;
                _exports->GetFileType = &HGetFileType;
                _exports->GetFileInformationByHandle = &HGetFileInformationByHandle;
                _exports->DuplicateHandle = &HDuplicateHandle;
                _exports->GetFileSize = &HGetFileSize;
                _exports->GetFileSizeEx = &HGetFileSizeEx;
                _exports->GetFileAttributesExW = &HGetFileAttributesExW;

                string gameDirectory = Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory;
                AssemblyLoadContext loadContext = AssemblyLoadContext.GetLoadContext(typeof(Proxy).Assembly)
                    ?? throw new InvalidOperationException("AppProxy has no assembly load context.");
                lib = loadContext.LoadFromAssemblyPath(Path.Combine(gameDirectory, "AppWrapper.dll"));
                t = lib.GetType("AppWrapper.Wrap");

                if (t != null)
                {
                    _mRun = t.GetMethod("Run", BindingFlags.Static | BindingFlags.Public);
                    _mShutdown = t.GetMethod("Shutdown", BindingFlags.Static | BindingFlags.Public);
                    _mHCreateFileW = t.GetMethod("HCreateFileW", BindingFlags.Static | BindingFlags.Public);
                    _mHReadFile = t.GetMethod("HReadFile", BindingFlags.Static | BindingFlags.Public);
                    _mHFindFirstFileW = t.GetMethod("HFindFirstFileW", BindingFlags.Static | BindingFlags.Public);
                    _mHFindFirstFileExW = t.GetMethod("HFindFirstFileExW", BindingFlags.Static | BindingFlags.Public);
                    _mHFindNextFileW = t.GetMethod("HFindNextFileW", BindingFlags.Static | BindingFlags.Public);
                    _mHFindClose = t.GetMethod("HFindClose", BindingFlags.Static | BindingFlags.Public);
                    _mHSetFilePointer = t.GetMethod("HSetFilePointer", BindingFlags.Static | BindingFlags.Public);
                    _mHSetFilePointerEx = t.GetMethod("HSetFilePointerEx", BindingFlags.Static | BindingFlags.Public);
                    _mHCloseHandle = t.GetMethod("HCloseHandle", BindingFlags.Static | BindingFlags.Public);
                    _mHGetFileType = t.GetMethod("HGetFileType", BindingFlags.Static | BindingFlags.Public);
                    _mHGetFileInformationByHandle = t.GetMethod("HGetFileInformationByHandle", BindingFlags.Static | BindingFlags.Public);
                    _mHDuplicateHandle = t.GetMethod("HDuplicateHandle", BindingFlags.Static | BindingFlags.Public);
                    _mHGetFileSize = t.GetMethod("HGetFileSize", BindingFlags.Static | BindingFlags.Public);
                    _mHGetFileSizeEx = t.GetMethod("HGetFileSizeEx", BindingFlags.Static | BindingFlags.Public);
                    _mHGetFileAttributesExW = t.GetMethod("HGetFileAttributesExW", BindingFlags.Static | BindingFlags.Public);
                    _createFileW = (CreateFileWHandler?)_mHCreateFileW?.CreateDelegate(typeof(CreateFileWHandler));
                    _readFile = (ReadFileHandler?)_mHReadFile?.CreateDelegate(typeof(ReadFileHandler));
                    _setFilePointerEx = (SetFilePointerExHandler?)_mHSetFilePointerEx?.CreateDelegate(typeof(SetFilePointerExHandler));
                }

                if (_mRun != null) _mRun.Invoke(null, new object[] { Process.GetCurrentProcess(), Path.Combine(gameDirectory, ".7thWrapperProfile") });
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.ToString());
                try
                {
                    string gameDirectory = Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory;
                    string logDirectory = gameDirectory;
                    if (Directory.Exists(@"D:\7h-ARM"))
                    {
                        logDirectory = @"D:\7h-ARM\logs";
                        Directory.CreateDirectory(logDirectory);
                    }
                    File.AppendAllText(Path.Combine(logDirectory, "AppProxy.log"), ex + Environment.NewLine);
                }
                catch { }
            }

            return 0;
        }

        [UnmanagedCallersOnly]
        public static void Shutdown()
        {
            try
            {
                if (_mShutdown != null) _mShutdown.Invoke(null, null);

                t = null;
                lib = null;
                _createFileW = null;
                _readFile = null;
                _setFilePointerEx = null;

                _exports->Shutdown = null;
                _exports->CreateFileW = null;
                _exports->ReadFile = null;
                _exports->FindFirstFileW = null;
                _exports->FindFirstFileExW = null;
                _exports->FindNextFileW = null;
                _exports->FindClose = null;
                _exports->SetFilePointer = null;
                _exports->SetFilePointerEx = null;
                _exports->CloseHandle = null;
                _exports->GetFileType = null;
                _exports->GetFileInformationByHandle = null;
                _exports->DuplicateHandle = null;
                _exports->GetFileSize = null;
                _exports->GetFileSizeEx = null;
                _exports->GetFileAttributesExW = null;

                System.GC.Collect();
                System.GC.WaitForPendingFinalizers();
                System.GC.WaitForFullGCComplete();
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.ToString());
            }
        }

        [UnmanagedCallersOnly]
        public static void* HCreateFileW(ushort* lpFileName, uint dwDesiredAccess, uint dwShareMode, void* lpSecurityAttributes, uint dwCreationDisposition, uint dwFlagsAndAttributes, void* hTemplateFile)
        {
            IntPtr ret = IntPtr.Zero;

            try
            {
                if (_createFileW != null) ret = _createFileW(new string((char*)lpFileName), (FileAccess)dwDesiredAccess, (FileShare)dwShareMode, new IntPtr(lpSecurityAttributes), (FileMode)dwCreationDisposition, (FileAttributes)dwFlagsAndAttributes, new IntPtr(hTemplateFile));
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.ToString());
            }

            return ret == IntPtr.Zero ? null : ret.ToPointer();
        }

        [UnmanagedCallersOnly]
        public static int HReadFile(void* handle, void* bytes, uint numBytesToRead, uint* numBytesRead, void* overlapped)
        {
            int ret = 0;

            try
            {
                if (_readFile != null) ret = _readFile(new IntPtr(handle), new IntPtr(bytes), numBytesToRead, new IntPtr(numBytesRead), new IntPtr(overlapped));
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.ToString());
            }

            return ret;
        }

        [UnmanagedCallersOnly]
        public static void* HFindFirstFileW(ushort* lpFileName, void* lpFindFileData)
        {
            IntPtr ret = IntPtr.Zero;

            try
            {
                if (_mHFindFirstFileW != null) ret = (IntPtr)(_mHFindFirstFileW.Invoke(null, new object[] { new string((char*)lpFileName), new IntPtr(lpFindFileData) }) ?? IntPtr.Zero);
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.ToString());
            }

            return ret == IntPtr.Zero ? null : ret.ToPointer();
        }

        [UnmanagedCallersOnly]
        public static void* HFindFirstFileExW(ushort* lpFileName, uint fInfoLevelId, void* lpFindFileData, uint fSearchOp, void* lpSearchFilter, uint dwAdditionalFlags)
        {
            IntPtr ret = IntPtr.Zero;

            try
            {
                if (_mHFindFirstFileExW != null) ret = (IntPtr)(_mHFindFirstFileExW.Invoke(null, new object[] { new string((char*)lpFileName), fInfoLevelId, new IntPtr(lpFindFileData), fSearchOp, new IntPtr(lpSearchFilter), dwAdditionalFlags }) ?? IntPtr.Zero);
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.ToString());
            }

            return ret == IntPtr.Zero ? null : ret.ToPointer();
        }

        [UnmanagedCallersOnly]
        public static int HFindNextFileW(void* hFindFile, void* lpFindFileData)
        {
            int ret = 0;

            try
            {
                if (_mHFindNextFileW != null) ret = (int)(_mHFindNextFileW.Invoke(null, new object[] { new IntPtr(hFindFile), new IntPtr(lpFindFileData) }) ?? 0);
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.ToString());
            }

            return ret;
        }

        [UnmanagedCallersOnly]
        public static int HFindClose(void* hFindFile)
        {
            int ret = 0;

            try
            {
                if (_mHFindClose != null) ret = (int)(_mHFindClose.Invoke(null, new object[] { new IntPtr(hFindFile) }) ?? 0);
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.ToString());
            }

            return ret;
        }

        [UnmanagedCallersOnly]
        public static uint HSetFilePointer(void* hFile, int lDistanceTomove, int* lpDistanceToMoveHigh, uint dwMoveMethod)
        {
            uint ret = uint.MaxValue;

            try
            {
                if (_mHSetFilePointer != null) ret = (uint)(_mHSetFilePointer.Invoke(null, new object[] { new IntPtr(hFile), lDistanceTomove, new IntPtr(lpDistanceToMoveHigh), dwMoveMethod }) ?? 0);
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.ToString());
            }

            return ret;
        }

        [UnmanagedCallersOnly]
        public static int HSetFilePointerEx(void* hFile, long liDistanceToMove, void* lpNewFilePointer, uint dwMoveMethod)
        {
            int ret = 0;

            try
            {
                if (_setFilePointerEx != null) ret = _setFilePointerEx(new IntPtr(hFile), liDistanceToMove, new IntPtr(lpNewFilePointer), dwMoveMethod);
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.ToString());
            }

            return ret;
        }

        [UnmanagedCallersOnly]
        public static int HCloseHandle(void* hObject)
        {
            int ret = 0;

            try
            {
                if (_mHCloseHandle != null) ret = (int)(_mHCloseHandle.Invoke(null, new object[] { new IntPtr(hObject) }) ?? 0);
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.ToString());
            }

            return ret;
        }

        [UnmanagedCallersOnly]
        public static uint HGetFileType(void* hFile)
        {
            uint ret = 0;

            try
            {
                if (_mHGetFileType != null) ret = (uint)(_mHGetFileType.Invoke(null, new object[] { new IntPtr(hFile) }) ?? 0);
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.ToString());
            }

            return ret;
        }

        [UnmanagedCallersOnly]
        public static int HGetFileInformationByHandle(void* hFile, void* lpFileInformation)
        {
            int ret = 0;

            try
            {
                if (_mHGetFileInformationByHandle != null) ret = (int)(_mHGetFileInformationByHandle.Invoke(null, new object[] { new IntPtr(hFile), new IntPtr(lpFileInformation) }) ?? 0);
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.ToString());
            }

            return ret;
        }

        [UnmanagedCallersOnly]
        public static int HDuplicateHandle(void* hSourceProcessHandle, void* hSourceHandle, void* hTargetProcessHandle, void** lpTargetHandle, uint dwDesiredAccess, int bInheritHandle, uint dwOptions)
        {
            int ret = 0;

            try
            {
                if (_mHDuplicateHandle != null) ret = (int)(_mHDuplicateHandle.Invoke(null, new object[] { new IntPtr(hSourceProcessHandle), new IntPtr(hSourceHandle), new IntPtr(hTargetProcessHandle), new IntPtr(lpTargetHandle), dwDesiredAccess, bInheritHandle, dwOptions }) ?? 0);
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.ToString());
            }

            return ret;
        }

        [UnmanagedCallersOnly]
        public static uint HGetFileSize(void* hFile, uint* lpFileSizeHigh)
        {
            uint ret = 0xFFFFFFFF;

            try
            {
                if (_mHGetFileSize != null) ret = (uint)(_mHGetFileSize.Invoke(null, new object[] { new IntPtr(hFile), new IntPtr(lpFileSizeHigh) }) ?? 0);
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.ToString());
            }

            return ret;
        }

        [UnmanagedCallersOnly]
        public static int HGetFileSizeEx(void* hFile, int* lpFileSize)
        {
            int ret = 0;

            try
            {
                if (_mHGetFileSizeEx != null) ret = (int)(_mHGetFileSizeEx.Invoke(null, new object[] { new IntPtr(hFile), new IntPtr(lpFileSize) }) ?? 0);
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.ToString());
            }

            return ret;
        }

        [UnmanagedCallersOnly]
        public static int HGetFileAttributesExW(ushort* lpFileName, uint fInfoLevelId, void* lpFileInformation)
        {
            int ret = 0;

            try
            {
                if (_mHGetFileAttributesExW != null) ret = (int)(_mHGetFileAttributesExW.Invoke(null, new object[] { new string((char*)lpFileName), fInfoLevelId, new IntPtr(lpFileInformation) }) ?? 0);
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.ToString());
            }

            return ret;
        }
    }
}
