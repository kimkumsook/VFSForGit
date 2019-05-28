using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using PrjFSLib.Linux.Interop;
using PrjFSLib.POSIX;
using static PrjFSLib.Linux.Interop.Errno;

namespace PrjFSLib.Linux
{
    public class LinuxVirtualizationInstance : VirtualizationInstance
    {
        private static readonly TimeSpan MountWaitTick = TimeSpan.FromSeconds(0.2);
        private static readonly TimeSpan MountWaitTotal = TimeSpan.FromSeconds(30);

        private ProjFS projfs;
        private int currentProcessId = Process.GetCurrentProcess().Id;
        private string virtualizationRoot;

        // We must hold a reference to the delegates to prevent garbage collection
        private ProjFS.EventHandler preventGCOnProjEventDelegate;
        private ProjFS.EventHandler preventGCOnNotifyEventDelegate;
        private ProjFS.EventHandler preventGCOnPermEventDelegate;

        public override Result StartVirtualizationInstance(
            string storageRootFullPath,
            string virtualizationRootFullPath,
            uint poolThreadCount)
        {
            if (this.projfs != null)
            {
                throw new InvalidOperationException();
            }

            int statResult = LinuxNative.Stat(virtualizationRootFullPath, out LinuxNative.StatBuffer stat);
            if (statResult != 0)
            {
                return Result.Invalid;
            }

            ulong priorDev = stat.Dev;

            ProjFS.Handlers handlers = new ProjFS.Handlers
            {
                HandleProjEvent = this.preventGCOnProjEventDelegate = new ProjFS.EventHandler(this.HandleProjEvent),
                HandleNotifyEvent = this.preventGCOnNotifyEventDelegate = new ProjFS.EventHandler(this.HandleNotifyEvent),
                HandlePermEvent = this.preventGCOnPermEventDelegate = new ProjFS.EventHandler(this.HandlePermEvent)
            };

            // determine whether storageRootFullPath contains only .git and .gitattributes

            string[] args;
            if (IsUninitializedMount(storageRootFullPath))
            {
                args = new string[] { "-o", "initial" };
            }
            else
            {
                args = new string[] { };
            }

//// DEBUG chrisd
            args = new string[] { "-o", "initial" };

//// DEBUG chrisd
//            this.projfs = ProjFS.New(
            ProjFS fs = ProjFS.New(
                storageRootFullPath,
                virtualizationRootFullPath,
                handlers,
                args);

            this.virtualizationRoot = virtualizationRootFullPath;

//// DEBUG chrisd
//            if (this.projfs == null)
            if (fs == null)
            {
                return Result.Invalid;
            }

//// DEBUG chrisd
//            if (this.projfs.Start() != 0)
            if (fs.Start() != 0)
            {
                // this.projfs.Stop();
                fs.Stop();
                this.projfs = null;
                return Result.Invalid;
            }

            Stopwatch watch = Stopwatch.StartNew();

            while (true)
            {
                statResult = LinuxNative.Stat(virtualizationRootFullPath, out stat);
                if (priorDev != stat.Dev)
                {
                    break;
                }

                Thread.Sleep(MountWaitTick);

                if (watch.Elapsed > MountWaitTotal)
                {
//// DEBUG chrisd
                    // this.projfs.Stop();
                    fs.Stop();
                    this.projfs = null;
                    return Result.Invalid;
                }
            }

//// DEBUG chrisd
            this.projfs = fs;
            return Result.Success;
        }

        public override void StopVirtualizationInstance()
        {
            if (this.projfs == null)
            {
                return;
            }

            this.projfs.Stop();
            this.projfs = null;
        }

        public override Result WriteFileContents(
            IntPtr fileHandle,
            byte[] bytes,
            uint byteCount)
        {
            int fd = Marshal.PtrToStructure<int>(fileHandle);

            if (!NativeFileWriter.TryWrite(fd, bytes, byteCount))
            {
                return Result.EIOError;
            }

            return Result.Success;
        }

        public override Result DeleteFile(
            string relativePath,
            UpdateType updateFlags,
            out UpdateFailureCause failureCause)
        {
            failureCause = UpdateFailureCause.NoFailure;
            if (string.IsNullOrEmpty(relativePath))
            {
                /* Our mount point directory can not be deleted; we would
                 * receive an EBUSY error.  Therefore we just return
                 * EDirectoryNotEmpty because that error is silently handled
                 * by our caller in GitIndexProjection, and this is the
                 * expected behavior (corresponding to the Mac implementation).
                 */
                return Result.EDirectoryNotEmpty;
            }

            string fullPath = Path.Combine(this.virtualizationRoot, relativePath);
            bool isDirectory = Directory.Exists(fullPath);
            Result result = Result.Success;
            if (!isDirectory)
            {
                // TODO(Linux): try to handle races with hydration?
                ProjectionState state;
                result = this.projfs.GetProjState(relativePath, out state);

                // also treat unknown state as full/dirty (e.g., for sockets)
                if ((result == Result.Success && state == ProjectionState.Full) ||
                    (result == Result.Invalid && state == ProjectionState.Unknown))
                {
                    failureCause = UpdateFailureCause.DirtyData;
                    return Result.EVirtualizationInvalidOperation;
                }
            }

            if (result == Result.Success)
            {
                result = RemoveFileOrDirectory(fullPath, isDirectory);
            }

            if (result == Result.EAccessDenied)
            {
                failureCause = UpdateFailureCause.ReadOnly;
            }
            else if (result == Result.EFileNotFound || result == Result.EPathNotFound)
            {
                return Result.Success;
            }

            return result;
        }

        public override Result WritePlaceholderDirectory(
            string relativePath)
        {
            return this.projfs.CreateProjDir(relativePath, Convert.ToUInt32("777", 8));
        }

        public override Result WritePlaceholderFile(
            string relativePath,
            byte[] providerId,
            byte[] contentId,
            ulong fileSize,
            uint fileMode)
        {
            if (providerId.Length != PlaceholderIdLength ||
                contentId.Length != PlaceholderIdLength)
            {
                throw new ArgumentException();
            }

            return this.projfs.CreateProjFile(
                relativePath,
                fileSize,
                fileMode,
                providerId,
                contentId);
        }

        public override Result WriteSymLink(
            string relativePath,
            string symLinkTarget)
        {
            return this.projfs.CreateProjSymlink(
                relativePath,
                symLinkTarget);
        }

        public override Result UpdatePlaceholderIfNeeded(
            string relativePath,
            byte[] providerId,
            byte[] contentId,
            ulong fileSize,
            uint fileMode,
            UpdateType updateFlags,
            out UpdateFailureCause failureCause)
        {
            if (providerId.Length != PlaceholderIdLength ||
                contentId.Length != PlaceholderIdLength)
            {
                throw new ArgumentException();
            }

            Result result = this.DeleteFile(relativePath, updateFlags, out failureCause);
            if (result != Result.Success)
            {
                return result;
            }

            // TODO(Linux): try to handle races with hydration?
            failureCause = UpdateFailureCause.NoFailure;
            return this.WritePlaceholderFile(relativePath, providerId, contentId, fileSize, fileMode);
        }

        public override Result ReplacePlaceholderFileWithSymLink(
            string relativePath,
            string symLinkTarget,
            UpdateType updateFlags,
            out UpdateFailureCause failureCause)
        {
            Result result = this.DeleteFile(relativePath, updateFlags, out failureCause);
            if (result != Result.Success)
            {
                return result;
            }

            // TODO(Linux): try to handle races with hydration?
            failureCause = UpdateFailureCause.NoFailure;
            return this.WriteSymLink(relativePath, symLinkTarget);
        }

        private static bool IsUninitializedMount(string dir)
        {
            bool foundDotGit = false,
                 foundDotGitattributes = false;

            foreach (string path in Directory.EnumerateFileSystemEntries(dir))
            {
                string file = Path.GetFileName(path);
                if (file == ".git")
                {
                    foundDotGit = true;
                }
                else if (file == ".gitattributes")
                {
                    foundDotGitattributes = true;
                }
                else
                {
                    return false;
                }
            }

            return foundDotGit && foundDotGitattributes;
        }

        private static Result RemoveFileOrDirectory(
            string fullPath,
            bool isDirectory)
        {
            try
            {
                if (isDirectory)
                {
                    Directory.Delete(fullPath);
                }
                else
                {
                    File.Delete(fullPath);
                }
            }
            catch (IOException ex) when (ex is DirectoryNotFoundException)
            {
                return Result.EPathNotFound;
            }
            catch (IOException ex) when (ex is FileNotFoundException)
            {
                return Result.EFileNotFound;
            }
            catch (IOException ex) when (ex.HResult == Errno.Constants.ENOTEMPTY)
            {
                return Result.EDirectoryNotEmpty;
            }
            catch (IOException)
            {
                return Result.EIOError;
            }
            catch (UnauthorizedAccessException)
            {
                return Result.EAccessDenied;
            }
            catch
            {
                return Result.Invalid;
            }

            return Result.Success;
        }

        private static string GetProcCmdline(int pid)
        {
            try
            {
                using (var stream = File.OpenText($"/proc/{pid}/cmdline"))
                {
                    string[] parts = stream.ReadToEnd().Split('\0');
                    return parts.Length > 0 ? parts[0] : string.Empty;
                }
            }
            catch
            {
                // process with given pid may have exited; nothing to be done
                return string.Empty;
            }
        }

        // TODO(Linux): replace with netstandard2.1 Marshal.PtrToStringUTF8()
        private static string PtrToStringUTF8(IntPtr ptr)
        {
            return Marshal.PtrToStringAnsi(ptr);
        }

        private bool IsProviderEvent(ProjFS.Event ev)
        {
            return (ev.Pid == this.currentProcessId);
        }

        private int HandleProjEvent(ref ProjFS.Event ev)
        {
            // ignore events triggered by own process to prevent deadlocks
            if (this.IsProviderEvent(ev))
            {
                return 0;
            }

//// DEBUG chrisd
            if (this.projfs == null)
            {
                return -Result.EDriverNotLoaded.ToErrno();
            }

            string triggeringProcessName = GetProcCmdline(ev.Pid);
            string relativePath = PtrToStringUTF8(ev.Path);

            Result result;

            if ((ev.Mask & ProjFS.Constants.PROJFS_ONDIR) != 0)
            {
                result = this.OnEnumerateDirectory(
                    commandId: 0,
                    relativePath: relativePath,
                    triggeringProcessId: ev.Pid,
                    triggeringProcessName: triggeringProcessName);
            }
            else
            {
                byte[] providerId = new byte[PlaceholderIdLength];
                byte[] contentId = new byte[PlaceholderIdLength];

                result = this.projfs.GetProjAttrs(
                    relativePath,
                    providerId,
                    contentId);

                if (result == Result.Success)
                {
                    unsafe
                    {
                        fixed (int* fileHandle = &ev.Fd)
                        {
                            result = this.OnGetFileStream(
                                commandId: 0,
                                relativePath: relativePath,
                                providerId: providerId,
                                contentId: contentId,
                                triggeringProcessId: ev.Pid,
                                triggeringProcessName: triggeringProcessName,
                                fileHandle: (IntPtr)fileHandle);
                        }
                    }
                }
            }

            return -result.ToErrno();
        }

        private int HandleNonProjEvent(ref ProjFS.Event ev, bool perm)
        {
            // ignore events triggered by own process to prevent deadlocks
            if (this.IsProviderEvent(ev))
            {
                return perm ? (int)ProjFS.Constants.PROJFS_ALLOW : 0;
            }

            bool isLink = (ev.Mask & ProjFS.Constants.PROJFS_ONLINK) != 0;
            NotificationType nt;

            if ((ev.Mask & ProjFS.Constants.PROJFS_DELETE_PERM) != 0)
            {
                nt = NotificationType.PreDelete;
            }
            else if ((ev.Mask & ProjFS.Constants.PROJFS_CLOSE_WRITE) != 0)
            {
                nt = NotificationType.FileModified;
            }
            else if ((ev.Mask & ProjFS.Constants.PROJFS_CREATE) != 0 && !isLink)
            {
                nt = NotificationType.NewFileCreated;
            }
            else if ((ev.Mask & ProjFS.Constants.PROJFS_MOVE) != 0)
            {
                nt = NotificationType.FileRenamed;
            }
            else if ((ev.Mask & ProjFS.Constants.PROJFS_CREATE) != 0 && isLink)
            {
                nt = NotificationType.HardLinkCreated;
            }
            else if ((ev.Mask & ProjFS.Constants.PROJFS_OPEN_PERM) != 0)
            {
                nt = NotificationType.PreConvertToFull;
            }
            else
            {
                return 0;
            }

            bool isDirectory = (ev.Mask & ProjFS.Constants.PROJFS_ONDIR) != 0;
            string relativePath;

            if (nt == NotificationType.FileRenamed ||
                nt == NotificationType.HardLinkCreated)
            {
                relativePath = PtrToStringUTF8(ev.TargetPath);
            }
            else
            {
                relativePath = PtrToStringUTF8(ev.Path);
            }

            Result result = this.OnNotifyOperation(
                relativePath: relativePath,
                isDirectory: isDirectory,
                notificationType: nt);

            int ret = -result.ToErrno();

            if (perm)
            {
                if (ret == 0)
                {
                    ret = (int)ProjFS.Constants.PROJFS_ALLOW;
                }
                else if (ret == -Errno.Constants.EPERM)
                {
                    ret = (int)ProjFS.Constants.PROJFS_DENY;
                }
            }

            return ret;
        }

        private int HandleNotifyEvent(ref ProjFS.Event ev)
        {
            return this.HandleNonProjEvent(ref ev, false);
        }

        private int HandlePermEvent(ref ProjFS.Event ev)
        {
            return this.HandleNonProjEvent(ref ev, true);
        }

        private Result OnNotifyOperation(
            string relativePath,
            bool isDirectory,
            NotificationType notificationType)
        {
            switch (notificationType)
            {
                case NotificationType.PreDelete:
                    return this.OnPreDelete(relativePath, isDirectory);

                case NotificationType.FileModified:
                    this.OnFileModified(relativePath);
                    return Result.Success;

                case NotificationType.NewFileCreated:
                    this.OnNewFileCreated(relativePath, isDirectory);
                    return Result.Success;

                case NotificationType.FileRenamed:
                    this.OnFileRenamed(relativePath, isDirectory);
                    return Result.Success;

                case NotificationType.HardLinkCreated:
                    this.OnHardLinkCreated(relativePath);
                    return Result.Success;

                case NotificationType.PreConvertToFull:
                    return this.OnFilePreConvertToFull(relativePath);
            }

            return Result.ENotYetImplemented;
        }

        private static unsafe class NativeFileWriter
        {
            public static bool TryWrite(int fd, byte[] bytes, uint byteCount)
            {
                 fixed (byte* bytesPtr = bytes)
                 {
                     byte* bytesIndexPtr = bytesPtr;

                     while (byteCount > 0)
                     {
                        long res = Write(fd, bytesIndexPtr, byteCount);
                        if (res == -1)
                        {
                            return false;
                        }

                        bytesIndexPtr += res;
                        byteCount -= (uint)res;
                    }
                }

                return true;
            }

            [DllImport("libc", EntryPoint = "write", SetLastError = true)]
            private static extern long Write(int fd, byte* buf, ulong count);
        }
    }
}
