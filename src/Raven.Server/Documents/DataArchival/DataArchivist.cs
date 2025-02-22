﻿using System;
using System.Linq;
using System.Threading.Tasks;
using Raven.Client.Documents.Operations.DataArchival;
using Raven.Client.ServerWide;
using Raven.Server.Background;
using Raven.Server.NotificationCenter.Notifications;
using Raven.Server.ServerWide.Context;
using Sparrow.Logging;
using Sparrow.Platform;

namespace Raven.Server.Documents.DataArchival;

public class DataArchivist : BackgroundWorkBase
{
    internal static int BatchSize = PlatformDetails.Is32Bits == false
        ? 4096
        : 1024;

    private readonly DocumentDatabase _database;
    private readonly TimeSpan _archivePeriod;

    public DataArchivalConfiguration DataArchivalConfiguration { get; }

    private DataArchivist(DocumentDatabase database, DataArchivalConfiguration dataArchivalConfiguration) : base(database.Name, database.DatabaseShutdown)
    {
        DataArchivalConfiguration = dataArchivalConfiguration;
        _database = database;
        _archivePeriod = TimeSpan.FromSeconds(DataArchivalConfiguration?.ArchiveFrequencyInSec ?? 60);
    }

    public static DataArchivist LoadConfiguration(DocumentDatabase database, DatabaseRecord dbRecord, DataArchivist dataArchivist)
    {
        try
        {
            if (dbRecord.DataArchival == null)
            {
                dataArchivist?.Dispose();
                return null;
            }

            if (dataArchivist != null)
            {
                // no changes
                if (Equals(dataArchivist.DataArchivalConfiguration, dbRecord.DataArchival))
                    return dataArchivist;
            }

            dataArchivist?.Dispose();

            var hasArchive = dbRecord.DataArchival?.Disabled == false;

            if (hasArchive == false)
                return null;

            var archivist = new DataArchivist(database, dbRecord.DataArchival);
            archivist.Start();
            return archivist;
        }
        catch (Exception e)
        {
            const string msg = "Cannot enable data archivist as the configuration record is not valid.";
            database.NotificationCenter.Add(AlertRaised.Create(
                database.Name,
                $"Data archival load configuration error in '{database.Name}' database", msg,
                AlertType.ArchivalConfigurationNotValid, NotificationSeverity.Error, database.Name));

            var logger = LoggingSource.Instance.GetLogger<DataArchivist>(database.Name);
            if (logger.IsOperationsEnabled)
                logger.Operations(msg, e);

            return null;
        }
    }

    protected override Task DoWork()
    {
        return DoArchiveWork();
    }

    private async Task DoArchiveWork()
    {
        while (DataArchivalConfiguration?.Disabled == false)
        {
            await WaitOrThrowOperationCanceled(_archivePeriod);

            await ArchiveDocs();
        }
    }

    internal Task ArchiveDocs(int? batchSize = null)
    {
        return ArchiveDocs(batchSize ?? BatchSize);
    }

    private async Task ArchiveDocs(int batchSize)
    {
        var currentTime = _database.Time.GetUtcNow();

        try
        {
            DatabaseTopology topology;
            string nodeTag;
            
            using (_database.ServerStore.ContextPool.AllocateOperationContext(out TransactionOperationContext serverContext))
            using (serverContext.OpenReadTransaction())
            {
                topology = _database.ServerStore.Cluster.ReadDatabaseTopology(serverContext, _database.Name);
                nodeTag = _database.ServerStore.NodeTag;
            }


            using (_database.DocumentsStorage.ContextPool.AllocateOperationContext(out DocumentsOperationContext context))
            {
                while (true)
                {
                    context.Reset();
                    context.Renew();

                    using (context.OpenReadTransaction())
                    {
                        var options = new BackgroundWorkParameters(context, currentTime, topology, nodeTag, batchSize);

                        var toArchive = _database.DocumentsStorage.DataArchivalStorage.GetDocuments(options, out var duration, CancellationToken);

                        if (toArchive == null || toArchive.Count == 0)
                            return;

                        var command = new ArchiveDocumentsCommand(toArchive, _database, currentTime);
                        await _database.TxMerger.Enqueue(command);

                        if (Logger.IsInfoEnabled)
                            Logger.Info($"Successfully archived {command.ArchivedDocsCount:#,#;;0} documents in {duration.ElapsedMilliseconds:#,#;;0} ms.");
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            // this will stop processing and return
        }
        catch (Exception e)
        {
            if (Logger.IsOperationsEnabled)
                Logger.Operations($"Failed to archive documents on {_database.Name} which are older than {currentTime}", e);
        }
    }
}
