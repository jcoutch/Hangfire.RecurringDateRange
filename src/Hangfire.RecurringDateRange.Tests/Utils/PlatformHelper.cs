using System;

#if !NET452
using System.Runtime.InteropServices;
#endif

namespace Hangfire.Core.Tests
{
    internal static class PlatformHelper
    {
        public static bool IsRunningOnWindows()
        {
#if NET452
            return Environment.OSVersion.Platform == PlatformID.Win32NT;
#else
            return RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
#endif
        }
    }
}
