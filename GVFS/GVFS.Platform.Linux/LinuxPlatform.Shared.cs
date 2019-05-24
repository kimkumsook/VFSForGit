using System;
using System.IO;
using GVFS.Platform.POSIX;

namespace GVFS.Platform.Linux
{
    public partial class LinuxPlatform
    {
        public const string DotGVFSRoot = ".vfsforgit";

        public static string GetDataRootForGVFSImplementation()
        {
            // TODO(Linux): determine installation location and data path
            string path = Environment.GetEnvironmentVariable("VFS4G_DATA_PATH");
            if (path == null)
            {
                path = "/var/run/vfsforgit";
            }

            return path;
        }

        public static string GetDataRootForGVFSComponentImplementation(string componentName)
        {
            return Path.Combine(GetDataRootForGVFSImplementation(), componentName);
        }

        public static bool TryGetGVFSEnlistmentRootImplementation(string directory, out string enlistmentRoot, out string errorMessage)
        {
            return POSIXPlatform.TryGetGVFSEnlistmentRootImplementation(directory, DotGVFSRoot, out enlistmentRoot, out errorMessage);
        }

        public static string GetNamedPipeNameImplementation(string enlistmentRoot)
        {
            return POSIXPlatform.GetNamedPipeNameImplementation(enlistmentRoot, DotGVFSRoot);
        }
    }
}
