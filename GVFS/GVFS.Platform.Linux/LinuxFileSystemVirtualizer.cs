using GVFS.Common;
using GVFS.Common.Git;
using GVFS.Common.Tracing;
using GVFS.Platform.POSIX;
using PrjFSLib.Linux;
using PrjFSLib.POSIX;

namespace GVFS.Platform.Linux
{
    public class LinuxFileSystemVirtualizer : POSIXFileSystemVirtualizer
    {
        public LinuxFileSystemVirtualizer(
            GVFSContext context,
            GVFSGitObjects gitObjects,
            VirtualizationInstance virtualizationInstance)
            : base(context, gitObjects)
        {
            this.virtualizationInstance = virtualizationInstance ?? new LinuxVirtualizationInstance();
        }

        public override void Stop()
        {
            this.virtualizationInstance.StopVirtualizationInstance();
            this.Context.Tracer.RelatedEvent(EventLevel.Informational, $"{nameof(this.Stop)}_StopRequested", metadata: null);
        }

        protected override bool TryStart(out string error)
        {
            error = string.Empty;

            // Callbacks
            this.virtualizationInstance.OnEnumerateDirectory = this.OnEnumerateDirectory;
            this.virtualizationInstance.OnGetFileStream = this.OnGetFileStream;
            this.virtualizationInstance.OnLogError = this.OnLogError;
            this.virtualizationInstance.OnFileModified = this.OnFileModified;
            this.virtualizationInstance.OnPreDelete = this.OnPreDelete;
            this.virtualizationInstance.OnNewFileCreated = this.OnNewFileCreated;
            this.virtualizationInstance.OnFileRenamed = this.OnFileRenamed;
            this.virtualizationInstance.OnHardLinkCreated = this.OnHardLinkCreated;
            this.virtualizationInstance.OnFilePreConvertToFull = this.NotifyFilePreConvertToFull;

            uint threadCount = (uint)Environment.ProcessorCount * 2;

            Result result = this.virtualizationInstance.StartVirtualizationInstance(
                this.Context.Enlistment.WorkingDirectoryBackingRoot,
                this.Context.Enlistment.WorkingDirectoryRoot,
                threadCount);

            // TODO(Linux): note that most start errors are not reported
            // because they can only be retrieved from projfs_stop() at present
            if (result != Result.Success)
            {
                this.Context.Tracer.RelatedError($"{nameof(this.virtualizationInstance.StartVirtualizationInstance)} failed: " + result.ToString("X") + "(" + result.ToString("G") + ")");
                error = "Failed to start virtualization instance (" + result.ToString() + ")";
                return false;
            }

            this.Context.Tracer.RelatedEvent(EventLevel.Informational, $"{nameof(this.TryStart)}_StartedVirtualization", metadata: null);
            return true;
        }

        private static string ConvertDotPath(string path)
        {
            if (path == ".")
            {
                path = string.Empty;
            }

            return path;
        }

        private Result OnEnumerateDirectory(
            ulong commandId,
            string relativePath,
            int triggeringProcessId,
            string triggeringProcessName)
        {
            return POSIXFileSystemVirtualizer.OnEnumerateDirectory(commandId, ConvertDotPath(relativePath), triggeringProcessId, triggeringProcessName);
        }
    }
}
