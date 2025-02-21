using System;
using System.Diagnostics;
using Raven.Client.Util;
using Sparrow.Json.Parsing;

namespace Raven.Client.Documents.Operations.Backups
{
    public abstract class BackupStatus
    {
        public DateTime? LastFullBackup { get; set; }

        public DateTime? LastIncrementalBackup { get; set; }

        public long? FullBackupDurationInMs { get; set; }

        public long? IncrementalBackupDurationInMs { get; set; }

        public string Exception { get; set; }

        public IDisposable UpdateStats(bool isFullBackup)
        {
            var now = SystemTime.UtcNow;
            var sw = Stopwatch.StartNew();

            return new DisposableAction(() =>
            {
                if (isFullBackup)
                {
                    LastFullBackup = now;
                    FullBackupDurationInMs = sw.ElapsedMilliseconds;
                }
                else
                {
                    LastIncrementalBackup = now;
                    IncrementalBackupDurationInMs = sw.ElapsedMilliseconds;
                }
            });
        }

        public virtual DynamicJsonValue ToJson()
        {
            return new DynamicJsonValue
            {
                [nameof(LastFullBackup)] = LastFullBackup,
                [nameof(LastIncrementalBackup)] = LastIncrementalBackup,
                [nameof(FullBackupDurationInMs)] = FullBackupDurationInMs,
                [nameof(IncrementalBackupDurationInMs)] = IncrementalBackupDurationInMs,
                [nameof(Exception)] = Exception
            };
        }
    }

    public sealed class LastRaftIndex
    {
        public long? LastEtag { get; set; }

        public DynamicJsonValue ToJson()
        {
            return new DynamicJsonValue
            {
                [nameof(LastEtag)] = LastEtag
            };
        }
    }

    public sealed class LocalBackup : BackupStatus
    {
        public string BackupDirectory { get; set; }
        public string FileName { get; set; }

        public bool TempFolderUsed { get; set; }

        public override DynamicJsonValue ToJson()
        {
            var json = base.ToJson();
            json[nameof(BackupDirectory)] = BackupDirectory;
            json[nameof(FileName)] = FileName;
            json[nameof(TempFolderUsed)] = TempFolderUsed;
            return json;
        }
    }

    public abstract class CloudUploadStatus : BackupStatus
    {
        protected CloudUploadStatus()
        {
            UploadProgress = new UploadProgress();
        }

        public bool Skipped { get; set; }

        public UploadProgress UploadProgress { get; set; }

        public override DynamicJsonValue ToJson()
        {
            var json = base.ToJson();
            json[nameof(Skipped)] = Skipped;
            json[nameof(UploadProgress)] = UploadProgress.ToJson();
            return json;
        }
    }

    public sealed class UploadToS3 : CloudUploadStatus
    {
        
    }

    public sealed class UploadToGlacier : CloudUploadStatus
    {

    }

    public sealed class UploadToAzure : CloudUploadStatus
    {

    }

    public sealed class UploadToGoogleCloud : CloudUploadStatus
    {

    }

    public sealed class UploadToFtp : CloudUploadStatus
    {

    }

    public sealed class UploadProgress
    {
        public UploadProgress()
        {
            UploadType = UploadType.Regular;
            _sw = Stopwatch.StartNew();
        }

        private readonly Stopwatch _sw;

        public UploadType UploadType { get; set; }

        public UploadState UploadState { get; private set; }

        public long UploadedInBytes { get; set; }

        public long TotalInBytes { get; set; }

        public double BytesPutsPerSec { get; set; }

        public long UploadTimeInMs => _sw.ElapsedMilliseconds;

        public void ChangeState(UploadState newState)
        {
            UploadState = newState;
            switch (newState)
            {
                case UploadState.PendingUpload:
                    _sw.Restart();
                    break;
                case UploadState.Done:
                    _sw.Stop();
                    break;
            }
        }

        public void SetTotal(long totalLength)
        {
            TotalInBytes = totalLength;
        }

        public void UpdateUploaded(long length)
        {
            UploadedInBytes += length;
        }

        public void SetUploaded(long length)
        {
            UploadedInBytes = length;
        }

        public void ChangeType(UploadType newType)
        {
            UploadType = newType;
        }

        public DynamicJsonValue ToJson()
        {
            return new DynamicJsonValue(GetType())
            {
                [nameof(UploadType)] = UploadType,
                [nameof(UploadState)] = UploadState,
                [nameof(UploadedInBytes)] = UploadedInBytes,
                [nameof(TotalInBytes)] = TotalInBytes,
                [nameof(BytesPutsPerSec)] = BytesPutsPerSec,
                [nameof(UploadTimeInMs)] = UploadTimeInMs
            };
        }
    }

    public enum UploadState
    {
        PendingUpload,
        Uploading,
        PendingResponse,
        Done
    }

    public enum UploadType
    {
        Regular,
        Chunked
    }
}
