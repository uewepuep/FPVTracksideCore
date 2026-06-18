using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using OpenCvSharp;
using Tools;

namespace Timing.Aruco
{
    /// <summary>
    /// OpenCvSharp's built-in P/Invoke loader only probes the application base directory and the OS
    /// default search paths — it does NOT look in runtimes/&lt;rid&gt;/native/, which is where the
    /// bundled libOpenCvSharpExtern.dylib (and the OpenCV dependency dylibs it loads) actually live
    /// for the Mac build. That is why a fresh osx-arm64 publish throws DllNotFoundException until the
    /// dylibs are copied next to the executable.
    ///
    /// Instead of dumping ~280MB of dylibs into the output root, we register a DllImportResolver that
    /// resolves "OpenCvSharpExtern" from runtimes/&lt;rid&gt;/native/. Loading the dylib by full path
    /// lets dyld resolve its dependencies via @loader_path against the same folder, so the whole
    /// OpenCV dylib set keeps working from runtimes/osx-arm64/native/.
    ///
    /// Registered as a module initializer so the resolver is in place before OpenCvSharp's
    /// NativeMethods static constructor fires on the first Cv2 call.
    /// </summary>
    internal static class OpenCvNativeResolver
    {
        private const string LibraryName = "OpenCvSharpExtern";

        // CA2255: a module initializer here is intentional — it must run before OpenCvSharp's
        // NativeMethods static constructor, and Timing is an app-internal library, not a public NuGet.
#pragma warning disable CA2255
        [ModuleInitializer]
        internal static void Register()
#pragma warning restore CA2255
        {
            // Windows ships the native DLL via OpenCvSharp4.runtime.win and resolves it correctly,
            // so only the Unix builds need this redirect.
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return;

            try
            {
                NativeLibrary.SetDllImportResolver(typeof(Cv2).Assembly, Resolve);
            }
            catch (InvalidOperationException)
            {
                // A resolver is already registered for this assembly — nothing more to do.
            }
        }

        private static IntPtr Resolve(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
        {
            if (!string.Equals(libraryName, LibraryName, StringComparison.Ordinal))
                return IntPtr.Zero; // Not ours — let the default resolver handle it.

            string baseDir = AppContext.BaseDirectory;
            string fileName = RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
                ? "libOpenCvSharpExtern.dylib"
                : "libOpenCvSharpExtern.so";

            foreach (string candidate in EnumerateCandidates(baseDir, fileName))
            {
                if (File.Exists(candidate) && NativeLibrary.TryLoad(candidate, out IntPtr handle))
                {
                    Logger.TimingLog?.Log(null, "[ArUco-Debug] resolver loaded OpenCvSharpExtern from " + candidate);
                    return handle;
                }
            }

            // Nothing matched — fall back to OpenCvSharp's default resolution (and its error message).
            return IntPtr.Zero;
        }

        private static IEnumerable<string> EnumerateCandidates(string baseDir, string fileName)
        {
            // The actual publish RID first, then the known Unix RIDs (RuntimeIdentifier can come back
            // RID-agnostic for framework-dependent builds), then the output root as a last resort.
            yield return Path.Combine(baseDir, "runtimes", RuntimeInformation.RuntimeIdentifier, "native", fileName);
            yield return Path.Combine(baseDir, "runtimes", "osx-arm64", "native", fileName);
            yield return Path.Combine(baseDir, "runtimes", "osx-x64", "native", fileName);
            yield return Path.Combine(baseDir, "runtimes", "linux-x64", "native", fileName);
            yield return Path.Combine(baseDir, fileName);
        }
    }
}
