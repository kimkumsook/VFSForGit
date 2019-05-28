using System;

namespace PrjFSLib.POSIX
{
    public abstract class VirtualizationInstance
    {
        public const int PlaceholderIdLength = 128;

        // References held to these delegates via class properties
        public virtual EnumerateDirectoryCallback OnEnumerateDirectory { get; set; }
        public virtual GetFileStreamCallback OnGetFileStream { get; set; }
        public virtual LogErrorCallback OnLogError { get; set; }

        public virtual NotifyFileModified OnFileModified { get; set; }
        public virtual NotifyFilePreConvertToFullEvent OnFilePreConvertToFull { get; set; }
        public virtual NotifyPreDeleteEvent OnPreDelete { get; set; }
        public virtual NotifyNewFileCreatedEvent OnNewFileCreated { get; set; }
        public virtual NotifyFileRenamedEvent OnFileRenamed { get; set; }
        public virtual NotifyHardLinkCreatedEvent OnHardLinkCreated { get; set; }

        public abstract Result StartVirtualizationInstance(
            string storageRootFullPath,
            string abstractizationRootFullPath,
            uint poolThreadCount);

        public abstract void StopVirtualizationInstance();

        public abstract Result WriteFileContents(
            IntPtr fileHandle,
            byte[] bytes,
            uint byteCount);

        public abstract Result DeleteFile(
            string relativePath,
            UpdateType updateFlags,
            out UpdateFailureCause failureCause);

        public abstract Result WritePlaceholderDirectory(
            string relativePath);

        public abstract Result WritePlaceholderFile(
            string relativePath,
            byte[] providerId,
            byte[] contentId,
            ulong fileSize,
            uint fileMode);

        public abstract Result WriteSymLink(
            string relativePath,
            string symLinkTarget);

        public abstract Result UpdatePlaceholderIfNeeded(
            string relativePath,
            byte[] providerId,
            byte[] contentId,
            ulong fileSize,
            uint fileMode,
            UpdateType updateFlags,
            out UpdateFailureCause failureCause);

        public abstract Result ReplacePlaceholderFileWithSymLink(
            string relativePath,
            string symLinkTarget,
            UpdateType updateFlags,
            out UpdateFailureCause failureCause);

        public virtual Result CompleteCommand(
            ulong commandId,
            Result result)
        {
            throw new NotImplementedException();
        }

        public virtual Result ConvertDirectoryToPlaceholder(
            string relativeDirectoryPath)
        {
            throw new NotImplementedException();
        }
    }
}
