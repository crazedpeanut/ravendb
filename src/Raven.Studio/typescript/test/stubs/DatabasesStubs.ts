﻿import nonShardedDatabase from "models/resources/nonShardedDatabase";
import shardedDatabase from "models/resources/shardedDatabase";
import DetailedDatabaseStatistics = Raven.Client.Documents.Operations.DetailedDatabaseStatistics;
import EssentialDatabaseStatistics = Raven.Client.Documents.Operations.EssentialDatabaseStatistics;
import StudioDatabaseInfo = Raven.Server.Web.System.Processors.Studio.StudioDatabasesHandlerForGetDatabases.StudioDatabaseInfo;
import DatabaseGroupNodeStatus = Raven.Client.ServerWide.Operations.DatabaseGroupNodeStatus;
import StudioDatabaseState = Raven.Server.Web.System.Processors.Studio.StudioDatabasesHandlerForGetDatabasesState.StudioDatabaseState;
import RefreshConfiguration = Raven.Client.Documents.Operations.Refresh.RefreshConfiguration;
import DataArchival = Raven.Client.Documents.Operations.DataArchival.DataArchivalConfiguration;
import ExpirationConfiguration = Raven.Client.Documents.Operations.Expiration.ExpirationConfiguration;
import RevisionsConfiguration = Raven.Client.Documents.Operations.Revisions.RevisionsConfiguration;
import RevisionsCollectionConfiguration = Raven.Client.Documents.Operations.Revisions.RevisionsCollectionConfiguration;
import SorterDefinition = Raven.Client.Documents.Queries.Sorting.SorterDefinition;
import AnalyzerDefinition = Raven.Client.Documents.Indexes.Analysis.AnalyzerDefinition;
import document from "models/database/documents/document";

export class DatabasesStubs {
    private static genericDatabaseInfo(name: string): StudioDatabaseInfo {
        return {
            Name: name,
            IsEncrypted: false,
            LockMode: "Unlock",
            DeletionInProgress: {},
            Sharding: null,
            IndexesCount: 0,
            NodesTopology: {
                PriorityOrder: [],
                Members: [
                    {
                        NodeTag: "A",
                        ResponsibleNode: null,
                        NodeUrl: "http://a.ravendb",
                    },
                ],
                Promotables: [],
                Rehabs: [],
                Status: {
                    A: DatabasesStubs.statusOk(),
                },
                DynamicNodesDistribution: false,
            },
            IsDisabled: false,
            HasRefreshConfiguration: false,
            HasExpirationConfiguration: false,
            HasDataArchivalConfiguration: false,
            HasRevisionsConfiguration: false,
            StudioEnvironment: "None",
            IsSharded: false,
        };
    }

    static nonShardedSingleNodeDatabaseDto() {
        return DatabasesStubs.genericDatabaseInfo("db1");
    }

    static nonShardedSingleNodeDatabase() {
        const dto = DatabasesStubs.nonShardedSingleNodeDatabaseDto();
        const firstNodeTag = dto.NodesTopology.Members[0].NodeTag;
        return new nonShardedDatabase(dto, ko.observable(firstNodeTag));
    }

    static nonShardedClusterDatabaseDto() {
        const dbInfo = DatabasesStubs.genericDatabaseInfo("db1");
        dbInfo.NodesTopology.Members.push({
            NodeTag: "B",
            NodeUrl: "http://b.ravendb",
            ResponsibleNode: null,
        });
        dbInfo.NodesTopology.PriorityOrder = [];
        dbInfo.NodesTopology.Members.push({
            NodeTag: "C",
            NodeUrl: "http://c.ravendb",
            ResponsibleNode: null,
        });
        dbInfo.NodesTopology.Status["B"] = DatabasesStubs.statusOk();
        dbInfo.NodesTopology.Status["C"] = DatabasesStubs.statusOk();
        return dbInfo;
    }

    static nonShardedClusterDatabase() {
        const dto = DatabasesStubs.nonShardedClusterDatabaseDto();
        const firstNodeTag = dto.NodesTopology.Members[0].NodeTag;
        return new nonShardedDatabase(dto, ko.observable(firstNodeTag));
    }

    static shardedDatabaseDto(): StudioDatabaseInfo {
        const dbInfo = DatabasesStubs.genericDatabaseInfo("sharded");
        dbInfo.NodesTopology = null;
        dbInfo.IsSharded = true;
        dbInfo.IndexesCount = 5;
        dbInfo.Sharding = {
            Shards: {
                [0]: {
                    Members: [
                        {
                            NodeTag: "A",
                            NodeUrl: "http://a.ravendb",
                            ResponsibleNode: null,
                        },
                        {
                            NodeTag: "B",
                            NodeUrl: "http://b.ravendb",
                            ResponsibleNode: null,
                        },
                    ],
                    Rehabs: [],
                    Promotables: [],
                    PriorityOrder: [],
                    Status: {
                        A: DatabasesStubs.statusOk(),
                        B: DatabasesStubs.statusOk(),
                    },
                },
                [1]: {
                    Members: [
                        {
                            NodeTag: "B",
                            NodeUrl: "http://b.ravendb",
                            ResponsibleNode: null,
                        },
                        {
                            NodeTag: "C",
                            NodeUrl: "http://c.ravendb",
                            ResponsibleNode: null,
                        },
                    ],
                    Rehabs: [],
                    Promotables: [],
                    PriorityOrder: [],
                    Status: {
                        B: DatabasesStubs.statusOk(),
                        C: DatabasesStubs.statusOk(),
                    },
                },
                [2]: {
                    Members: [
                        {
                            NodeTag: "A",
                            NodeUrl: "http://a.ravendb",
                            ResponsibleNode: null,
                        },
                        {
                            NodeTag: "C",
                            NodeUrl: "http://c.ravendb",
                            ResponsibleNode: null,
                        },
                    ],
                    Rehabs: [],
                    Promotables: [],
                    PriorityOrder: [],
                    Status: {
                        A: DatabasesStubs.statusOk(),
                        C: DatabasesStubs.statusOk(),
                    },
                },
            },
            Orchestrator: {
                NodesTopology: {
                    Members: [
                        {
                            NodeTag: "A",
                            NodeUrl: "http://a.ravendb",
                            ResponsibleNode: null,
                        },
                        {
                            NodeTag: "B",
                            NodeUrl: "http://b.ravendb",
                            ResponsibleNode: null,
                        },
                        {
                            NodeTag: "C",
                            NodeUrl: "http://c.ravendb",
                            ResponsibleNode: null,
                        },
                    ],
                    Promotables: [],
                    Rehabs: [],
                    PriorityOrder: [],
                    Status: {
                        A: DatabasesStubs.statusOk(),
                        B: DatabasesStubs.statusOk(),
                        C: DatabasesStubs.statusOk(),
                    },
                },
            },
        } as any;

        return dbInfo;
    }

    static shardedDatabase() {
        const shardedDtos = DatabasesStubs.shardedDatabaseDto();
        return new shardedDatabase(shardedDtos, ko.observable("A"));
    }

    static essentialStats(): EssentialDatabaseStatistics {
        return {
            CountOfTimeSeriesSegments: 5,
            CountOfTombstones: 3,
            CountOfAttachments: 10,
            CountOfDocumentsConflicts: 5,
            CountOfRevisionDocuments: 12,
            CountOfDocuments: 1_234_567,
            CountOfIndexes: 17,
            CountOfCounterEntries: 1_453,
            CountOfConflicts: 83,
            Indexes: [],
        };
    }

    static studioDatabaseState(dbName: string): StudioDatabaseState {
        return {
            Name: dbName,
            UpTime: "00:05:00",
            IndexingStatus: "Running",
            LoadError: null,
            BackupInfo: null,
            DocumentsCount: 1024,
            Alerts: 1,
            PerformanceHints: 2,
            IndexingErrors: 3,
            TotalSize: {
                SizeInBytes: 5,
                HumaneSize: "5 Bytes",
            },
            TempBuffersSize: {
                SizeInBytes: 2,
                HumaneSize: "2 Bytes",
            },
            DatabaseStatus: "Online",
        };
    }

    static detailedStats(): DetailedDatabaseStatistics {
        const essential = DatabasesStubs.essentialStats();
        return {
            ...essential,
            CountOfIdentities: 17,
            CountOfCompareExchange: 38,
            DatabaseChangeVector:
                "A:2568-F9I6Egqwm0Kz+K0oFVIR9Q, A:13366-IG4VwBTOnkqoT/uwgm2OQg, A:2568-OSKWIRBEDEGoAxbEIiFJeQ, A:8807-jMR/KF8hz0uMKFDXnmrQJA",
            CountOfTimeSeriesDeletedRanges: 9,
            Is64Bit: true,
            NumberOfTransactionMergerQueueOperations: 0,
            DatabaseId: "jMR/KF8hz0uMKFDXnmrQJA",
            CountOfCompareExchangeTombstones: 44,
            SizeOnDisk: {
                HumaneSize: "295.44 MBytes",
                SizeInBytes: 309788672,
            },
            TempBuffersSizeOnDisk: {
                HumaneSize: "17.19 MBytes",
                SizeInBytes: 18022400,
            },
            CountOfUniqueAttachments: essential.CountOfAttachments - 2,
            Pager: "Voron.Impl.Paging.RvnMemoryMapPager",
            StaleIndexes: [],
            Indexes: [],
        };
    }

    private static statusOk(): DatabaseGroupNodeStatus {
        return {
            LastStatus: "Ok",
            LastError: null,
        };
    }

    static refreshConfiguration(): RefreshConfiguration {
        return {
            Disabled: false,
            RefreshFrequencyInSec: 129599,
        };
    }

    static expirationConfiguration(): ExpirationConfiguration {
        return {
            Disabled: false,
            DeleteFrequencyInSec: 129599,
        };
    }

    static tombstonesState(): TombstonesStateOnWire {
        return {
            MinAllDocsEtag: 9223372036854776000,
            MinAllTimeSeriesEtag: 9223372036854776000,
            MinAllCountersEtag: 9223372036854776000,
            Results: [
                {
                    Collection: "Attachments.Tombstones",
                    Documents: {
                        Component: null,
                        Etag: 9223372036854776000,
                    },
                    TimeSeries: {
                        Component: null,
                        Etag: 9223372036854776000,
                    },
                    Counters: {
                        Component: null,
                        Etag: 9223372036854776000,
                    },
                },
                {
                    Collection: "Revisions.Tombstones",
                    Documents: {
                        Component: null,
                        Etag: 9223372036854776000,
                    },
                    TimeSeries: {
                        Component: null,
                        Etag: 9223372036854776000,
                    },
                    Counters: {
                        Component: null,
                        Etag: 9223372036854776000,
                    },
                },
                {
                    Collection: "docs",
                    Documents: {
                        Component: "Index 'test'",
                        Etag: 0,
                    },
                    TimeSeries: {
                        Component: null,
                        Etag: 9223372036854776000,
                    },
                    Counters: {
                        Component: null,
                        Etag: 9223372036854776000,
                    },
                },
            ],
            PerSubscriptionInfo: [
                {
                    Identifier: "Index 'test'",
                    Type: "Documents",
                    Collection: "Docs",
                    Etag: 0,
                },
            ],
        };
    }

    static revisionsConfiguration(): RevisionsConfiguration {
        return {
            Default: null,
            Collections: {
                Categories: {
                    Disabled: true,
                    MinimumRevisionsToKeep: 16,
                    MinimumRevisionAgeToKeep: "65.00:00:00",
                    PurgeOnDelete: true,
                    MaximumRevisionsToDeleteUponDocumentUpdate: 80,
                },
                Shippers: {
                    Disabled: false,
                    MinimumRevisionsToKeep: null,
                    MinimumRevisionAgeToKeep: null,
                    PurgeOnDelete: false,
                    MaximumRevisionsToDeleteUponDocumentUpdate: null,
                },
            },
        };
    }

    static revisionsForConflictsConfiguration(): RevisionsCollectionConfiguration {
        return {
            Disabled: true,
            MinimumRevisionsToKeep: null,
            MinimumRevisionAgeToKeep: "55.00:00:00",
            PurgeOnDelete: false,
            MaximumRevisionsToDeleteUponDocumentUpdate: 100,
        };
    }

    static customAnalyzers(): AnalyzerDefinition[] {
        return [{ Code: "database-analyzer-code-1", Name: "First Database analyzer" }];
    }

    static customSorters(): SorterDefinition[] {
        return [{ Code: "database-sorter-code-1", Name: "First Database sorter" }];
    }

    static dataArchivalConfiguration(): DataArchival {
        return {
            Disabled: false,
            ArchiveFrequencyInSec: 65,
        };
    }

    static documentsCompressionConfiguration(): Raven.Client.ServerWide.DocumentsCompressionConfiguration {
        return {
            Collections: ["Shippers"],
            CompressAllCollections: false,
            CompressRevisions: true,
        };
    }
    static emptyConnectionStrings(): Raven.Client.Documents.Operations.ConnectionStrings.GetConnectionStringsResult {
        return {
            ElasticSearchConnectionStrings: {},
            OlapConnectionStrings: {},
            QueueConnectionStrings: {},
            RavenConnectionStrings: {},
            SqlConnectionStrings: {},
        };
    }

    static connectionStrings(): Raven.Client.Documents.Operations.ConnectionStrings.GetConnectionStringsResult {
        return {
            RavenConnectionStrings: {
                "raven-name (used by task)": {
                    Type: "Raven",
                    Name: "raven-name (used by task)",
                    Database: "some-db",
                    TopologyDiscoveryUrls: ["http://test"],
                },
            },
            SqlConnectionStrings: {
                "sql-name": {
                    Type: "Sql",
                    Name: "sql-name",
                    ConnectionString: "some-connection-string",
                    FactoryName: "System.Data.SqlClient",
                },
            },
            OlapConnectionStrings: {
                "olap-name": {
                    Type: "Olap",
                    Name: "olap-name",
                    LocalSettings: {
                        Disabled: false,
                        GetBackupConfigurationScript: null,
                        FolderPath: "/bin",
                    },
                    S3Settings: null,
                    AzureSettings: null,
                    GlacierSettings: null,
                    GoogleCloudSettings: null,
                    FtpSettings: null,
                },
            },
            ElasticSearchConnectionStrings: {
                "elasticsearch-name": {
                    Type: "ElasticSearch",
                    Name: "elasticsearch-name",
                    Nodes: ["http://test"],
                    EnableCompatibilityMode: false,
                    Authentication: {
                        Basic: null,
                        ApiKey: null,
                        Certificate: null,
                    },
                },
            },
            QueueConnectionStrings: {
                "kafka-name": {
                    Type: "Queue",
                    Name: "kafka-name",
                    BrokerType: "Kafka",
                    KafkaConnectionSettings: {
                        BootstrapServers: "test:0",
                        UseRavenCertificate: false,
                        ConnectionOptions: {},
                    },
                    RabbitMqConnectionSettings: null,
                },
                "rabbitmq-name": {
                    Type: "Queue",
                    Name: "rabbitmq-name",
                    BrokerType: "RabbitMq",
                    KafkaConnectionSettings: null,
                    RabbitMqConnectionSettings: {
                        ConnectionString: "some-connection-string",
                    },
                },
            },
        };
    }

    static nodeConnectionTestErrorResult(): Raven.Server.Web.System.NodeConnectionTestResult {
        return {
            Success: false,
            HTTPSuccess: false,
            TcpServerUrl: null,
            Log: [],
            Error: "System.UriFormatException: Invalid URI: The format of the URI could not be determined.\n   at System.Uri.CreateThis(String uri, Boolean dontEscape, UriKind uriKind, UriCreationOptions& creationOptions)\n   at System.Uri..ctor(String uriString)\n   at Raven.Server.Documents.ETL.Providers.Queue.QueueBrokerConnectionHelper.CreateRabbitMqConnection(RabbitMqConnectionSettings settings) in D:\\Builds\\RavenDB-6.0-Nightly\\20231123-0200\\src\\Raven.Server\\Documents\\ETL\\Providers\\Queue\\QueueBrokerConnectionHelper.cs:line 80",
        };
    }

    static nodeConnectionTestSuccessResult(): Raven.Server.Web.System.NodeConnectionTestResult {
        return {
            Success: true,
            HTTPSuccess: true,
            TcpServerUrl: null,
            Log: [],
            Error: null,
        };
    }

    static conflictSolverConfiguration(): Raven.Client.ServerWide.ConflictSolver {
        return {
            ResolveByCollection: {
                Categories: {
                    Script: `
var maxRecord = 0;
for (var i = 0; i < docs.length; i++) {
    maxRecord = Math.max(docs[i].MaxRecord, maxRecord);   
}
docs[0].MaxRecord = maxRecord;

return docs[0];`,
                    LastModifiedTime: "2024-01-03T12:13:16.7455603Z",
                },
                Shippers: {
                    Script: `
var maxPrice = 0;
for (var i = 0; i < docs.length; i++) {
    maxPrice = Math.max(docs[i].PricePerUnit, maxPrice);   
}
docs[0].PricePerUnit = maxPrice;

return docs[0];`,
                    LastModifiedTime: "2024-01-04T12:13:16.7455603Z",
                },
            },
            ResolveToLatest: true,
        };
    }

    static databaseRecord(): document {
        return new document({
            DatabaseName: "drec",
            Disabled: false,
            Encrypted: false,
            EtagForBackup: 0,
            DeletionInProgress: {},
            RollingIndexes: {},
            DatabaseState: "Normal",
            LockMode: "Unlock",
            Topology: {
                Members: ["A"],
                Promotables: [],
                Rehabs: [],
                PredefinedMentors: {},
                DemotionReasons: {},
                PromotablesStatus: {},
                Stamp: {
                    Index: 512,
                    Term: 1,
                    LeadersTicks: -2,
                },
                DynamicNodesDistribution: false,
                ReplicationFactor: 1,
                PriorityOrder: [],
                NodesModifiedAt: "2024-01-02T12:47:22.6904463Z",
                DatabaseTopologyIdBase64: "V/OB7JEtLEiazn6QID9RQw",
                ClusterTransactionIdBase64: "VtiBjDGBe0uajuJ7lArnbw",
            },
            Sharding: null,
            ConflictSolverConfig: null,
            DocumentsCompression: {
                Collections: [],
                CompressAllCollections: false,
                CompressRevisions: true,
            },
            Sorters: {},
            Analyzers: {},
            Indexes: {},
            IndexesHistory: {},
            AutoIndexes: {},
            Settings: {},
            Revisions: null,
            TimeSeries: null,
            RevisionsForConflicts: null,
            Expiration: null,
            Refresh: null,
            DataArchival: null,
            Integrations: null,
            PeriodicBackups: [],
            ExternalReplications: [],
            SinkPullReplications: [],
            HubPullReplications: [],
            RavenConnectionStrings: {},
            SqlConnectionStrings: {},
            OlapConnectionStrings: {},
            ElasticSearchConnectionStrings: {},
            QueueConnectionStrings: {},
            RavenEtls: [],
            SqlEtls: [],
            ElasticSearchEtls: [],
            OlapEtls: [],
            QueueEtls: [],
            QueueSinks: [],
            Client: null,
            Studio: null,
            TruncatedClusterTransactionCommandsCount: 0,
            UnusedDatabaseIds: [],
            Etag: 512,
        });
    }
}
