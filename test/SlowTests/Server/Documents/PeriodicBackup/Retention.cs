﻿using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Raven.Client.Documents.Operations;
using Raven.Client.Documents.Operations.Backups;
using Raven.Client.Documents.Smuggler;
using Raven.Client.Exceptions;
using Raven.Client.Util;
using Raven.Server.Documents.PeriodicBackup;
using Raven.Server.Documents.PeriodicBackup.Aws;
using Raven.Tests.Core.Utils.Entities;
using SlowTests.Server.Documents.PeriodicBackup.Restore;
using Tests.Infrastructure;
using Voron.Util.Settings;
using Xunit;
using Xunit.Abstractions;

namespace SlowTests.Server.Documents.PeriodicBackup
{
    public class Retention : RestoreFromS3
    {
        public Retention(ITestOutputHelper output) : base(output)
        {
        }

        private static readonly SemaphoreSlim Locker = new SemaphoreSlim(1, 1);

        public static async ValueTask<IDisposable> SkipMinimumBackupAgeToKeepValidationAsync()
        {
            await Locker.WaitAsync();

            Debug.Assert(BackupConfigurationHelper.SkipMinimumBackupAgeToKeepValidation == false);
            BackupConfigurationHelper.SkipMinimumBackupAgeToKeepValidation = true;

            return new DisposableAction(() =>
            {
                BackupConfigurationHelper.SkipMinimumBackupAgeToKeepValidation = false;
                Locker.Release();
            });
        }

        [Theory, Trait("Category", "Smuggler")]
        [InlineData(7, 3, false)]
        [InlineData(10, 3, true)]
        [InlineData(7, 3, false, "/E/G/O/R/../../../..")]
        public async Task can_delete_backups_by_date(int backupAgeInSeconds, int numberOfBackupsToCreate, bool checkIncremental, string suffix = null)
        {
            using (await SkipMinimumBackupAgeToKeepValidationAsync())
            {
                var backupPath = NewDataPath(suffix: "BackupFolder");

                if (suffix != null)
                {
                    backupPath += suffix;
                }

                await CanDeleteBackupsByDate(backupAgeInSeconds, numberOfBackupsToCreate,
                    (configuration, _) =>
                    {
                        configuration.LocalSettings = new LocalSettings
                        {
                            FolderPath = backupPath
                        };
                    },
                    _ =>
                    {
                        backupPath = PathUtil.ToFullPath(backupPath);
                        var directories = Directory.GetDirectories(backupPath)
                            .Where(x => Directory.GetFiles(x).Any(BackupUtils.IsFullBackupOrSnapshot));

                        return Task.FromResult(directories.Count());
                    }, timeout: 15000, checkIncremental);
            }
        }

        [AmazonS3RetryTheory]
        [InlineData(7, 3, false)]
        [InlineData(10, 3, true)]
        public async Task can_delete_backups_by_date_s3(int backupAgeInSeconds, int numberOfBackupsToCreate, bool checkIncremental)
        {
            using (await SkipMinimumBackupAgeToKeepValidationAsync())
            {
                var settings = GetS3Settings();

                await CanDeleteBackupsByDate(backupAgeInSeconds, numberOfBackupsToCreate,
                    (configuration, databaseName) =>
                    {
                        configuration.S3Settings = settings;
                    },
                    async databaseName =>
                    {
                        using (var cts = new CancellationTokenSource(TimeSpan.FromMinutes(5)))
                        using (var client = new RavenAwsS3Client(settings, DefaultConfiguration, cancellationToken: cts.Token))
                        {
                            var folders = await client.ListObjectsAsync($"{client.RemoteFolderName}/", "/", listFolders: true);
                            return folders.FileInfoDetails.Count;
                        }
                    }, timeout: 120000, checkIncremental);
            }
        }

        [AzureRetryTheory, Trait("Category", "Smuggler")]
        [InlineData(7, 3, false)]
        [InlineData(10, 3, true)]
        public async Task can_delete_backups_by_date_azure(int backupAgeInSeconds, int numberOfBackupsToCreate, bool checkIncremental)
        {
            using (await SkipMinimumBackupAgeToKeepValidationAsync())
            using (var holder = new Azure.AzureClientHolder(AzureRetryFactAttribute.AzureSettings))
            {
                await CanDeleteBackupsByDate(backupAgeInSeconds, numberOfBackupsToCreate,
                        (configuration, databaseName) =>
                        {
                            configuration.AzureSettings = holder.Settings;
                        },
                        async databaseName =>
                        {

                            var folders = await holder.Client.ListBlobsAsync($"{holder.Client.RemoteFolderName}/", delimiter: "/", listFolders: true);
                            return folders.List.Count();
                        }, timeout: 120000, checkIncremental);
            }
        }

        [Fact, Trait("Category", "Smuggler")]
        public async Task configuration_validation()
        {
            await Locker.WaitAsync();

            try
            {
                Assert.False(BackupConfigurationHelper.SkipMinimumBackupAgeToKeepValidation);

                using (var store = GetDocumentStore())
                {
                    var config = Backup.CreateBackupConfiguration(incrementalBackupFrequency: "30 3 L * ?", retentionPolicy: new RetentionPolicy
                    {
                        MinimumBackupAgeToKeep = TimeSpan.FromDays(-5)
                    });
                    var error = await Assert.ThrowsAsync<RavenException>(() => store.Maintenance.SendAsync(new UpdatePeriodicBackupOperation(config)));
                    Assert.True(error.Message.Contains($"{nameof(RetentionPolicy.MinimumBackupAgeToKeep)} must be positive"));

                    config.RetentionPolicy.MinimumBackupAgeToKeep = TimeSpan.FromHours(12);
                    error = await Assert.ThrowsAsync<RavenException>(() => store.Maintenance.SendAsync(new UpdatePeriodicBackupOperation(config)));
                    Assert.True(error.Message.Contains($"{nameof(RetentionPolicy.MinimumBackupAgeToKeep)} must be bigger than one day"));
                }
            }
            finally
            {
                Locker.Release();
            }
        }

        private async Task CanDeleteBackupsByDate(
            int backupAgeInSeconds,
            int numberOfBackupsToCreate,
            Action<PeriodicBackupConfiguration, string> modifyConfiguration,
            Func<string, Task<int>> getDirectoriesCount,
            int timeout, bool checkIncremental = false)
        {
            var minimumBackupAgeToKeep = TimeSpan.FromSeconds(backupAgeInSeconds);

            using (var store = GetDocumentStore())
            {
                var config = Backup.CreateBackupConfiguration(incrementalBackupFrequency: "30 3 L * ?", retentionPolicy: new RetentionPolicy
                {
                    MinimumBackupAgeToKeep = minimumBackupAgeToKeep
                });
                modifyConfiguration(config, store.Database);

                var backupTaskId = (await store.Maintenance.SendAsync(new UpdatePeriodicBackupOperation(config))).TaskId;

                var userId = "";
                for (var i = 0; i < numberOfBackupsToCreate; i++)
                {

                    using (var session = store.OpenAsyncSession())
                    {
                        var user = new User { Name = "Grisha" };
                        await session.StoreAsync(user);
                        userId = user.Id;
                        await session.SaveChangesAsync();
                    }

                    // create full backup
                    var etagForFullBackup = store.Maintenance.Send(new GetStatisticsOperation()).LastDocEtag;
                    await Backup.RunBackupAndReturnStatusAsync(Server, backupTaskId, store, isFullBackup: true, expectedEtag: etagForFullBackup, timeout: timeout);

                    using (var session = store.OpenAsyncSession())
                    {
                        var user = await session.LoadAsync<User>(userId);
                        user.Age = 33;
                        await session.SaveChangesAsync();
                    }

                    // create incremental backup
                    var etagForIncBackup = store.Maintenance.Send(new GetStatisticsOperation()).LastDocEtag;
                    Assert.NotEqual(etagForFullBackup, etagForIncBackup);
                    await Backup.RunBackupAndReturnStatusAsync(Server, backupTaskId, store, isFullBackup: false, expectedEtag: etagForIncBackup, timeout: timeout);
                }

                await Task.Delay(minimumBackupAgeToKeep + TimeSpan.FromSeconds(3));

                if (checkIncremental)
                {
                    using (var session = store.OpenAsyncSession())
                    {
                        var user = await session.LoadAsync<User>(userId);
                        user.Name = "Egor";
                        user.Age = 322;
                        await session.SaveChangesAsync();
                    }

                    // create incremental backup with retention policy
                    var etagForIncBackup = store.Maintenance.Send(new GetStatisticsOperation()).LastDocEtag;
                    await Backup.RunBackupAndReturnStatusAsync(Server, backupTaskId, store, isFullBackup: false, expectedEtag: etagForIncBackup, timeout: timeout);
                }

                var sp1 = Stopwatch.StartNew();
                using (var session = store.OpenAsyncSession())
                {
                    await session.StoreAsync(new User { Name = "Grisha" });
                    await session.SaveChangesAsync();
                }
                sp1.Stop();
                var sp2 = Stopwatch.StartNew();
                var etag = store.Maintenance.Send(new GetStatisticsOperation()).LastDocEtag;
                sp2.Stop();
                var sp3 = Stopwatch.StartNew();
                var status = await Backup.RunBackupAndReturnStatusAsync(Server, backupTaskId, store, isFullBackup: true, expectedEtag: etag, timeout: timeout);
                sp3.Stop();

                var expectedNumberOfDirectories = checkIncremental ? 2 : 1;
                var gracePeriod = config.LocalSettings == null
                    ? (int)TimeSpan.FromSeconds(30).TotalMilliseconds
                    : 0;

                var directoriesCount = await WaitForValueAsync(async () => await getDirectoriesCount(store.Database),
                    expectedVal: expectedNumberOfDirectories,
                    timeout: gracePeriod,
                    interval: (int)TimeSpan.FromSeconds(1).TotalMilliseconds);

                Assert.True(expectedNumberOfDirectories == directoriesCount,
                    $"ExpectedNumberOfDirectories: {expectedNumberOfDirectories}, ActualNumberOfDirectories: {directoriesCount}, SaveChanges() duration: {sp1.Elapsed}, GetStatisticsOperation duration: {sp2.Elapsed}, RunBackupAndReturnStatusAsync duration: {sp3.Elapsed}," +
                    $" Backup duration: {status.DurationInMs}, LocalRetentionDurationInMs: {status.LocalRetentionDurationInMs}");

                if (PeriodicBackupConfiguration.CanBackupUsing(config.LocalSettings))
                    Assert.NotNull(status.LocalRetentionDurationInMs);
            }
        }
    }
}
