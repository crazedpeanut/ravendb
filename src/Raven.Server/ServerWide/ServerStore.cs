﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Lucene.Net.Search;
using Raven.Client.Util;
using Raven.Client.Exceptions.Server;
using Raven.Client.Extensions;
using Raven.Client.Http;
using Raven.Client.Json;
using Raven.Client.Server;
using Raven.Client.Server.ETL;
using Raven.Server.Commercial;
using Raven.Server.Config;
using Raven.Server.Documents;
using Raven.Server.Documents.ETL;
using Raven.Server.NotificationCenter;
using Raven.Server.Rachis;
using Raven.Server.NotificationCenter.Notifications;
using Raven.Server.NotificationCenter.Notifications.Details;
using Raven.Server.NotificationCenter.Notifications.Server;
using Raven.Server.ServerWide.Commands;
using Raven.Server.ServerWide.Commands.ETL;
using Raven.Server.ServerWide.Commands.PeriodicBackup;
using Raven.Server.ServerWide.Context;
using Raven.Server.ServerWide.Maintenance;
using Raven.Server.Utils;
using Sparrow;
using Sparrow.Json;
using Sparrow.Json.Parsing;
using Voron;
using Sparrow.Logging;
using Sparrow.LowMemory;
using Sparrow.Utils;

namespace Raven.Server.ServerWide
{
    /// <summary>
    /// Persistent store for server wide configuration, such as cluster settings, database configuration, etc
    /// </summary>
    public class ServerStore : IDisposable
    {
        private const string ResourceName = nameof(ServerStore);

        private static readonly Logger Logger = LoggingSource.Instance.GetLogger<ServerStore>(ResourceName);

        private readonly CancellationTokenSource _shutdownNotification = new CancellationTokenSource();

        public CancellationToken ServerShutdown => _shutdownNotification.Token;

        private StorageEnvironment _env;

        private readonly NotificationsStorage _notificationsStorage;

        private RequestExecutor _clusterRequestExecutor;

        public readonly RavenConfiguration Configuration;
        private readonly RavenServer _ravenServer;
        public readonly DatabasesLandlord DatabasesLandlord;
        public readonly NotificationCenter.NotificationCenter NotificationCenter;
        public readonly LicenseManager LicenseManager;
        public readonly FeedbackSender FeedbackSender;

        private readonly TimeSpan _frequencyToCheckForIdleDatabases;

        public ServerStore(RavenConfiguration configuration, RavenServer ravenServer)
        {
            Configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));

            _ravenServer = ravenServer;

            DatabasesLandlord = new DatabasesLandlord(this);

            _notificationsStorage = new NotificationsStorage(ResourceName);

            NotificationCenter = new NotificationCenter.NotificationCenter(_notificationsStorage, ResourceName, ServerShutdown);

            LicenseManager = new LicenseManager(NotificationCenter);

            FeedbackSender = new FeedbackSender();

            DatabaseInfoCache = new DatabaseInfoCache();

            _frequencyToCheckForIdleDatabases = Configuration.Databases.FrequencyToCheckForIdle.AsTimeSpan;

        }

        public RavenServer RavenServer => _ravenServer;

        public DatabaseInfoCache DatabaseInfoCache { get; set; }

        public TransactionContextPool ContextPool;

        public long LastRaftCommitEtag
        {
            get
            {
                using (ContextPool.AllocateOperationContext(out TransactionOperationContext context))
                using (context.OpenReadTransaction())
                    return _engine.GetLastCommitIndex(context);
            }
        }

        public ClusterStateMachine Cluster => _engine.StateMachine;
        public string LeaderTag => _engine.LeaderTag;

        public string NodeTag => _engine.Tag;
        public RachisConsensus.State CurrentState => _engine.CurrentState;

        public bool Disposed => _disposed;

        private Timer _timer;
        private RachisConsensus<ClusterStateMachine> _engine;
        private bool _disposed;
        public RachisConsensus<ClusterStateMachine> Engine => _engine;

        public ClusterMaintenanceSupervisor ClusterMaintenanceSupervisor;

        public Dictionary<string, ClusterNodeStatusReport> ClusterStats()
        {
            if (_engine.LeaderTag != NodeTag)
                throw new NotLeadingException($"Stats can be requested only from the raft leader {_engine.LeaderTag}");
            return ClusterMaintenanceSupervisor?.GetStats();
        }

        public async Task ClusterMaintenanceSetupTask()
        {
            while (true)
            {
                try
                {
                    if (_engine.LeaderTag != NodeTag)
                    {
                        await _engine.WaitForState(RachisConsensus.State.Leader)
                            .WithCancellation(_shutdownNotification.Token);
                        continue;
                    }
                    using (ClusterMaintenanceSupervisor = new ClusterMaintenanceSupervisor(this, _engine.Tag, _engine.CurrentTerm))
                    using (new ClusterObserver(this, ClusterMaintenanceSupervisor, _engine, ContextPool, ServerShutdown))
                    {
                        var oldNodes = new Dictionary<string, string>();
                        while (_engine.LeaderTag == NodeTag)
                        {
                            var topologyChangedTask = _engine.GetTopologyChanged();
                            ClusterTopology clusterTopology;
                            using (ContextPool.AllocateOperationContext(out TransactionOperationContext context))
                            using (context.OpenReadTransaction())
                            {
                                clusterTopology = _engine.GetTopology(context);
                            }
                            var newNodes = clusterTopology.AllNodes;
                            var nodesChanges = ClusterTopology.DictionaryDiff(oldNodes, newNodes);
                            oldNodes = newNodes;
                            foreach (var node in nodesChanges.removedValues)
                            {
                                ClusterMaintenanceSupervisor.RemoveFromCluster(node.Key);
                            }
                            foreach (var node in nodesChanges.addedValues)
                            {
                                var task = ClusterMaintenanceSupervisor.AddToCluster(node.Key, clusterTopology.GetUrlFromTag(node.Key)).ContinueWith(t =>
                                {
                                    if (Logger.IsInfoEnabled)
                                        Logger.Info($"ClusterMaintenanceSupervisor() => Failed to add to cluster node key = {node.Key}", t.Exception);
                                }, TaskContinuationOptions.OnlyOnFaulted);
                                GC.KeepAlive(task);
                            }

                            var leaderChanged = _engine.WaitForLeaveState(RachisConsensus.State.Leader);
                            if (await Task.WhenAny(topologyChangedTask, leaderChanged)
                                    .WithCancellation(_shutdownNotification.Token) == leaderChanged)
                                break;
                        }
                    }
                }
                catch (TaskCanceledException)
                {// ServerStore dispose?
                    throw;
                }
                catch (Exception)
                {
                    //
                }
            }
        }

        public ClusterTopology GetClusterTopology(TransactionOperationContext context)
        {
            return _engine.GetTopology(context);
        }

        public async Task AddNodeToClusterAsync(string nodeUrl, string nodeTag = null, bool validateNotInTopology = true)
        {
            await _engine.AddToClusterAsync(nodeUrl, nodeTag, validateNotInTopology).WithCancellation(_shutdownNotification.Token);
        }

        public async Task RemoveFromClusterAsync(string nodeTag)
        {
            await _engine.RemoveFromClusterAsync(nodeTag).WithCancellation(_shutdownNotification.Token);
        }

        public void Initialize()
        {
            LowMemoryNotification.Initialize(ServerShutdown,
                Configuration.Memory.LowMemoryDetection.GetValue(SizeUnit.Bytes),
                Configuration.Memory.PhysicalRatioForLowMemDetection);

            if (Logger.IsInfoEnabled)
                Logger.Info("Starting to open server store for " + (Configuration.Core.RunInMemory ? "<memory>" : Configuration.Core.DataDirectory.FullPath));

            var path = Configuration.Core.DataDirectory.Combine("System");
            var storeAlertForLateRaise = new List<AlertRaised>();

            var options = Configuration.Core.RunInMemory
                ? StorageEnvironmentOptions.CreateMemoryOnly()
                : StorageEnvironmentOptions.ForPath(path.FullPath);

            options.OnNonDurableFileSystemError += (obj, e) =>
            {
                var alert = AlertRaised.Create("Non Durable File System - System Storage",
                    e.Message,
                    AlertType.NonDurableFileSystem,
                    NotificationSeverity.Warning,
                    "NonDurable Error System",
                    details: new MessageDetails { Message = e.Details });
                if (NotificationCenter.IsInitialized)
                {
                    NotificationCenter.Add(alert);
                }
                else
                {
                    storeAlertForLateRaise.Add(alert);
                }
            };

            options.OnRecoveryError += (obj, e) =>
            {
                var alert = AlertRaised.Create("Recovery Error - System Storage",
                    e.Message,
                    AlertType.NonDurableFileSystem,
                    NotificationSeverity.Error,
                    "Recovery Error System");
                if (NotificationCenter.IsInitialized)
                {
                    NotificationCenter.Add(alert);
                }
                else
                {
                    storeAlertForLateRaise.Add(alert);
                }
            };

            try
            {
                if (MemoryInformation.IsSwappingOnHddInsteadOfSsd())
                {
                    var alert = AlertRaised.Create("Swap Storage Type Warning",
                        "OS swapping on at least one HDD drive while there is at least one SSD drive on this system. " +
                        "This can cause a slowdown, consider moving swap-partition/pagefile to SSD",
                        AlertType.SwappingHddInsteadOfSsd,
                        NotificationSeverity.Warning);
                    if (NotificationCenter.IsInitialized)
                    {
                        NotificationCenter.Add(alert);
                    }
                    else
                    {
                        storeAlertForLateRaise.Add(alert);
                    }
                }
            }
            catch (Exception e)
            {
                // the above should not throw, but we mask it in case it does (as it reads IO parameters) - this alert is just a nice-to-have warning
                if (Logger.IsInfoEnabled)
                    Logger.Info("An error occurred while trying to determine Is Swapping On Hdd Instead Of Ssd", e);
            }

            options.SchemaVersion = 2;
            options.ForceUsing32BitsPager = Configuration.Storage.ForceUsing32BitsPager;
            try
            {
                StorageEnvironment.MaxConcurrentFlushes = Configuration.Storage.MaxConcurrentFlushes;

                try
                {
                    _env = new StorageEnvironment(options);
                }
                catch (Exception e)
                {
                    throw new ServerLoadFailureException("Failed to load system storage " + Environment.NewLine + $"At {options.BasePath}", e);
                }
            }
            catch (Exception e)
            {
                if (Logger.IsOperationsEnabled)
                    Logger.Operations(
                        "Could not open server store for " + (Configuration.Core.RunInMemory ? "<memory>" : Configuration.Core.DataDirectory.FullPath), e);
                options.Dispose();
                throw;
            }

            if (Configuration.Queries.MaxClauseCount != null)
                BooleanQuery.MaxClauseCount = Configuration.Queries.MaxClauseCount.Value;

            ContextPool = new TransactionContextPool(_env);


            _engine = new RachisConsensus<ClusterStateMachine>();
            _engine.Initialize(_env, Configuration.Cluster);

            _engine.StateMachine.DatabaseChanged += DatabasesLandlord.ClusterOnDatabaseChanged;
            _engine.StateMachine.DatabaseChanged += OnDatabaseChanged;
            _engine.StateMachine.DatabaseValueChanged += DatabasesLandlord.ClusterOnDatabaseValueChanged;

            _engine.TopologyChanged += OnTopologyChanged;
            _engine.StateChanged += OnStateChanged;

            _timer = new Timer(IdleOperations, null, _frequencyToCheckForIdleDatabases, TimeSpan.FromDays(7));
            _notificationsStorage.Initialize(_env, ContextPool);
            DatabaseInfoCache.Initialize(_env, ContextPool);

            NotificationCenter.Initialize();
            foreach (var alertRaised in storeAlertForLateRaise)
            {
                NotificationCenter.Add(alertRaised);
            }
            LicenseManager.Initialize(_env, ContextPool);

            using (ContextPool.AllocateOperationContext(out TransactionOperationContext context))
            {
                context.OpenReadTransaction();
                foreach (var db in _engine.StateMachine.ItemsStartingWith(context, "db/", 0, int.MaxValue))
                {
                    DatabasesLandlord.ClusterOnDatabaseChanged(this, (db.Item1, 0, "Init"));
                }
            }

            Task.Run(ClusterMaintenanceSetupTask, ServerShutdown);
        }

        private void OnStateChanged(object sender, RachisConsensus.State e)
        {
            using (ContextPool.AllocateOperationContext(out TransactionOperationContext context))
            using (context.OpenReadTransaction())
            {
                NotificationCenter.Add(ClusterTopologyChanged.Create(GetClusterTopology(context), LeaderTag, NodeTag));
            }
        }

        private void OnTopologyChanged(object sender, ClusterTopology topologyJson)
        {
            NotificationCenter.Add(ClusterTopologyChanged.Create(topologyJson, LeaderTag, NodeTag));
        }

        private void OnDatabaseChanged(object sender, (string dbName, long index, string type) t)
        {
            switch (t.type)
            {
                case nameof(DeleteDatabaseCommand):
                    NotificationCenter.Add(DatabaseChanged.Create(t.dbName, DatabaseChangeType.Delete));
                    break;
                case nameof(AddDatabaseCommand):
                    NotificationCenter.Add(DatabaseChanged.Create(t.dbName, DatabaseChangeType.Put));
                    break;
                case nameof(UpdateTopologyCommand):
                    NotificationCenter.Add(DatabaseChanged.Create(t.dbName, DatabaseChangeType.Update));
                    break;
            }

            //TODO: send different commands to studio when necessary
        }

        public IEnumerable<string> GetSecretKeysNames(TransactionOperationContext context)
        {
            var tree = context.Transaction.InnerTransaction.ReadTree("SecretKeys");
            if (tree == null)
                yield break;

            using (var it = tree.Iterate(prefetch: false))
            {
                if (it.Seek(Slices.BeforeAllKeys) == false)
                    yield break;
                do
                {

                    yield return it.CurrentKey.ToString();

                } while (it.MoveNext());
            }

        }

        public unsafe void PutSecretKey(
            TransactionOperationContext context,
            string name,
            byte[] key,
            bool overwrite = false /*Be careful with this one, overwriting a key might be disastrous*/)
        {
            Debug.Assert(context.Transaction != null);
            if (key.Length != 256 / 8)
                throw new ArgumentException($"Key size must be 256 bits, but was {key.Length * 8}", nameof(key));

            byte[] existingKey;
            try
            {
                existingKey = GetSecretKey(context, name);
            }
            catch (Exception)
            {
                // failure to read the key might be because the user password has changed
                // in this case, we ignore the existence of the key and overwrite it
                existingKey = null;
            }
            if (existingKey != null)
            {
                fixed (byte* pKey = key)
                fixed (byte* pExistingKey = existingKey)
                {
                    bool areEqual = Sparrow.Memory.Compare(pKey, pExistingKey, key.Length) == 0;
                    Sodium.ZeroMemory(pExistingKey, key.Length);
                    if (areEqual)
                    {
                        Sodium.ZeroMemory(pKey, key.Length);
                        return;
                    }
                }
            }

            var tree = context.Transaction.InnerTransaction.CreateTree("SecretKeys");
            var record = Cluster.ReadDatabase(context, name);

            if (overwrite == false && tree.Read(name) != null)
                throw new InvalidOperationException($"Attempt to overwrite secret key {name}, which isn\'t permitted (you\'ll lose access to the encrypted db).");

            if (record != null && record.Encrypted == false)
                throw new InvalidOperationException($"Cannot modify key {name} where there is an existing database that is not encrypted");

            var hashLen = Sodium.crypto_generichash_bytes_max();
            var hash = new byte[hashLen + key.Length];
            fixed (byte* pHash = hash)
            fixed (byte* pKey = key)
            {
                try
                {
                    if (Sodium.crypto_generichash(pHash, (UIntPtr)hashLen, pKey, (ulong)key.Length, null, UIntPtr.Zero) != 0)
                        throw new InvalidOperationException("Failed to hash key");

                    Sparrow.Memory.Copy(pHash + hashLen, pKey, key.Length);

                    var entropy = Sodium.GenerateRandomBuffer(256);

                    var protectedData = SecretProtection.Protect(hash, entropy);

                    var ms = new MemoryStream();
                    ms.Write(entropy, 0, entropy.Length);
                    ms.Write(protectedData, 0, protectedData.Length);
                    ms.Position = 0;

                    tree.Add(name, ms);
                }
                finally
                {
                    Sodium.ZeroMemory(pHash, hash.Length);
                    Sodium.ZeroMemory(pKey, key.Length);
                }
            }
        }


        public unsafe byte[] GetSecretKey(TransactionOperationContext context, string name)
        {
            Debug.Assert(context.Transaction != null);

            var tree = context.Transaction.InnerTransaction.ReadTree("SecretKeys");

            var readResult = tree?.Read(name);
            if (readResult == null)
                return null;

            const int numberOfBits = 256;
            var entropy = new byte[numberOfBits / 8];
            var reader = readResult.Reader;
            reader.Read(entropy, 0, entropy.Length);
            var protectedData = new byte[reader.Length - entropy.Length];
            reader.Read(protectedData, 0, protectedData.Length);

            var data = SecretProtection.Unprotect(protectedData, entropy);

            var hashLen = Sodium.crypto_generichash_bytes_max();

            fixed (byte* pData = data)
            fixed (byte* pHash = new byte[hashLen])
            {
                try
                {
                    if (Sodium.crypto_generichash(pHash, (UIntPtr)hashLen, pData + hashLen, (ulong)(data.Length - hashLen), null, UIntPtr.Zero) != 0)
                        throw new InvalidOperationException($"Unable to compute hash for {name}");

                    if (Sodium.sodium_memcmp(pData, pHash, (UIntPtr)hashLen) != 0)
                        throw new InvalidOperationException($"Unable to validate hash after decryption for {name}, user store changed?");

                    var buffer = new byte[data.Length - hashLen];
                    fixed (byte* pBuffer = buffer)
                    {
                        Sparrow.Memory.Copy(pBuffer, pData + hashLen, buffer.Length);
                    }
                    return buffer;
                }
                finally
                {
                    Sodium.ZeroMemory(pData, data.Length);
                }
            }
        }

        public void DeleteSecretKey(TransactionOperationContext context, string name)
        {
            Debug.Assert(context.Transaction != null);

            var record = Cluster.ReadDatabase(context, name);

            if (record != null)
                throw new InvalidOperationException($"Cannot delete key {name} where there is an existing database that require its usage");

            var tree = context.Transaction.InnerTransaction.CreateTree("SecretKeys");

            tree.Delete(name);
        }

        public Task<(long Etag, object Result)> DeleteDatabaseAsync(string db, bool hardDelete, string fromNode)
        {
            var deleteCommand = new DeleteDatabaseCommand(db)
            {
                HardDelete = hardDelete,
                FromNode = fromNode
            };
            return SendToLeaderAsync(deleteCommand);
        }

        public Task<(long Etag, object Result)> ModifyCustomFunctions(string dbName, string customFunctions)
        {
            var customFunctionsCommand = new ModifyCustomFunctionsCommand(dbName)
            {
                CustomFunctions = customFunctions
            };
            return SendToLeaderAsync(customFunctionsCommand);
        }

        public Task<(long Etag, object Result)> UpdateDatabaseWatcher(string dbName, DatabaseWatcher watcher)
        {
            var addWatcherCommand = new UpdateDatabaseWatcherCommand(dbName)
            {
                Watcher = watcher
            };
            return SendToLeaderAsync(addWatcherCommand);
        }

        public Task<(long Etag, object Result)> DeleteDatabaseWatcher(long taskId, string dbName)
        {
            var deleteWatcherCommand = new DeleteDatabaseWatcherCommand(taskId, dbName);

            return SendToLeaderAsync(deleteWatcherCommand);
        }

        public Task<(long Etag, object Result)> ModifyConflictSolverAsync(string dbName, ConflictSolver solver)
        {
            var conflictResolverCommand = new ModifyConflictSolverCommand(dbName)
            {
                Solver = solver
            };
            return SendToLeaderAsync(conflictResolverCommand);
        }

        public Task<(long Etag, object Result)> PutValueInClusterAsync<T>(PutValueCommand<T> cmd)
        {
            return SendToLeaderAsync(cmd);
        }

        public Task<(long Etag, object Result)> DeleteValueInClusterAsync(string key)
        {
            var deleteValueCommand = new DeleteValueCommand
            {
                Name = key
            };
            return SendToLeaderAsync(deleteValueCommand);
        }

        public Task<(long Etag, object Result)> ModifyDatabaseExpiration(TransactionOperationContext context, string name, BlittableJsonReaderObject configurationJson)
        {
            var editExpiration = new EditExpirationCommand(JsonDeserializationCluster.ExpirationConfiguration(configurationJson), name);
            return SendToLeaderAsync(editExpiration);
        }

        public async Task<(long, object)> ModifyPeriodicBackup(TransactionOperationContext context, string name, BlittableJsonReaderObject configurationJson)
        {
            var modifyPeriodicBackup = new UpdatePeriodicBackupCommand(JsonDeserializationCluster.PeriodicBackupConfiguration(configurationJson), name);
            return await SendToLeaderAsync(modifyPeriodicBackup);
        }

        public Task<(long, object)> DeletePeriodicBackup(TransactionOperationContext context, string name, BlittableJsonReaderObject taskIdJson)
        {
            taskIdJson.TryGet("TaskId", out long taskId);
            var editPeriodicBackup = new DeletePeriodicBackupCommand(taskId, name);
            return SendToLeaderAsync(editPeriodicBackup);
        }

        public async Task<(long, object)> AddEtl(TransactionOperationContext context, string databaseName, BlittableJsonReaderObject etlConfiguration, EtlType type)
        {
            UpdateDatabaseCommand command;

            switch (type)
            {
                case EtlType.Raven:
                    command = new AddRavenEtlCommand(JsonDeserializationCluster.RavenEtlConfiguration(etlConfiguration), databaseName);
                    break;
                case EtlType.Sql:
                    command = new AddSqlEtlCommand(JsonDeserializationCluster.SqlEtlConfiguration(etlConfiguration), databaseName);
                    break;
                default:
                    throw new NotSupportedException($"Unknown ETL configuration destination type: {type}");
            }

            return await SendToLeaderAsync(command);
        }

        public async Task<(long, object)> UpdateEtl(TransactionOperationContext context, string databaseName, long id, BlittableJsonReaderObject etlConfiguration, EtlType type)
        {
            UpdateDatabaseCommand command;

            switch (type)
            {
                case EtlType.Raven:
                    command = new UpdateRavenEtlCommand(id, JsonDeserializationCluster.RavenEtlConfiguration(etlConfiguration), databaseName);
                    break;
                case EtlType.Sql:
                    command = new UpdateSqlEtlCommand(id, JsonDeserializationCluster.SqlEtlConfiguration(etlConfiguration), databaseName);
                    break;
                default:
                    throw new NotSupportedException($"Unknown ETL configuration destination type: {type}");
            }

            return await SendToLeaderAsync(command);
        }

        public async Task<(long, object)> DeleteEtl(TransactionOperationContext context, string databaseName, long id, BlittableJsonReaderObject _, EtlType type)
        {
            var command = new DeleteEtlCommand(id, type, databaseName);

            return await SendToLeaderAsync(command);
        }

        public async Task<(long, object)> ToggleEtlState(TransactionOperationContext context, string databaseName, long id, BlittableJsonReaderObject _, EtlType type)
        {
            var command = new ToggleEtlStateCommand(id, type, databaseName);

            return await SendToLeaderAsync(command);
        }

        public Task<(long, object)> ModifyDatabaseVersioning(JsonOperationContext context, string name, BlittableJsonReaderObject configurationJson)
        {
            var editVersioning = new EditVersioningCommand(JsonDeserializationCluster.VersioningConfiguration(configurationJson), name);
            return SendToLeaderAsync(editVersioning);
        }

        public Guid GetServerId()
        {
            return _env.DbId;
        }

        public void Dispose()
        {
            if (_shutdownNotification.IsCancellationRequested || _disposed)
                return;

            lock (this)
            {
                if (_disposed)
                    return;

                try
                {
                    if (_shutdownNotification.IsCancellationRequested)
                        return;

                    _shutdownNotification.Cancel();
                    var toDispose = new List<IDisposable>
                    {
                        _engine,
                        NotificationCenter,
                        LicenseManager,
                        DatabasesLandlord,
                        _env,
                        ContextPool
                    };

                    var exceptionAggregator = new ExceptionAggregator(Logger, $"Could not dispose {nameof(ServerStore)}.");

                    foreach (var disposable in toDispose)
                        exceptionAggregator.Execute(() =>
                        {
                            try
                            {
                                disposable?.Dispose();
                            }
                            catch (ObjectDisposedException)
                            {
                                //we are disposing, so don't care
                            }
                        });

                    exceptionAggregator.Execute(() => _shutdownNotification.Dispose());

                    exceptionAggregator.ThrowIfNeeded();
                }
                finally
                {
                    _disposed = true;
                }
            }


        }

        public void IdleOperations(object state)
        {
            try
            {
                foreach (var db in DatabasesLandlord.DatabasesCache)
                {
                    try
                    {
                        if (db.Value.Status != TaskStatus.RanToCompletion)
                            continue;

                        var database = db.Value.Result;

                        if (DatabaseNeedsToRunIdleOperations(database))
                            database.RunIdleOperations();
                    }

                    catch (Exception e)
                    {
                        if (Logger.IsInfoEnabled)
                            Logger.Info("Error during idle operation run for " + db.Key, e);
                    }
                }

                try
                {
                    var maxTimeDatabaseCanBeIdle = Configuration.Databases.MaxIdleTime.AsTimeSpan;

                    var databasesToCleanup = DatabasesLandlord.LastRecentlyUsed
                        .Where(x => SystemTime.UtcNow - x.Value > maxTimeDatabaseCanBeIdle)
                        .Select(x => x.Key)
                        .ToArray();

                    foreach (var db in databasesToCleanup)
                    {
                        Task<DocumentDatabase> resourceTask;
                        if (DatabasesLandlord.DatabasesCache.TryGetValue(db, out resourceTask) &&
                            resourceTask != null &&
                            resourceTask.Status == TaskStatus.RanToCompletion &&
                            resourceTask.Result.PeriodicBackupRunner != null &&
                            resourceTask.Result.PeriodicBackupRunner.HasRunningBackups())
                        {
                            // there are running backups for this database
                            continue;
                        }

                        // intentionally inside the loop, so we get better concurrency overall
                        // since shutting down a database can take a while
                        DatabasesLandlord.UnloadDatabase(db, skipIfActiveInDuration: maxTimeDatabaseCanBeIdle, shouldSkip: database => database.Configuration.Core.RunInMemory);
                    }

                }
                catch (Exception e)
                {
                    if (Logger.IsInfoEnabled)
                        Logger.Info("Error during idle operations for the server", e);
                }
            }
            finally
            {
                try
                {
                    _timer.Change(_frequencyToCheckForIdleDatabases, TimeSpan.FromDays(7));
                }
                catch (ObjectDisposedException)
                {
                }
            }
        }

        private static bool DatabaseNeedsToRunIdleOperations(DocumentDatabase database)
        {
            var now = DateTime.UtcNow;

            var envs = database.GetAllStoragesEnvironment();

            var maxLastWork = DateTime.MinValue;

            foreach (var env in envs)
            {
                if (env.Environment.LastWorkTime > maxLastWork)
                    maxLastWork = env.Environment.LastWorkTime;
            }

            return ((now - maxLastWork).TotalMinutes > 5) || ((now - database.LastIdleTime).TotalMinutes > 10);
        }


        public Task<(long Etag, object Result)> WriteDatabaseRecordAsync(string databaseName, DatabaseRecord record, long? etag)
        {
            var addDatabaseCommand = new AddDatabaseCommand
            {
                Name = databaseName,
                Etag = etag,
                Record = record
            };

            return SendToLeaderAsync(addDatabaseCommand);
        }

        public void EnsureNotPassive()
        {
            if (_engine.CurrentState == RachisConsensus.State.Passive)
            {
                _engine.Bootstarp(EnsureValidExternalUrl(_ravenServer.WebUrls[0]));
            }
        }


        public static string EnsureValidExternalUrl(string url)
        {
            if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                switch (uri.Host)
                {
                    case "::":
                    case "::0":
                    case "0.0.0.0":
                        url = new UriBuilder(uri)
                        {
                            Host = Environment.MachineName
                        }.Uri.ToString();
                        break;
                }
            }
            return url.TrimEnd('/');
        }

        public Task<(long Etag, object Result)> PutCommandAsync(CommandBase cmd)
        {
            return _engine.PutAsync(cmd);
        }

        public bool IsLeader()
        {
            return _engine.CurrentState == RachisConsensus.State.Leader;
        }

        public Task<(long Etag, object Result)> SendToLeaderAsync(CommandBase cmd)
        {
            return SendToLeaderAsyncInternal(cmd);
        }

        public DynamicJsonArray GetClusterErrors()
        {
            return _engine.GetClusterErrorsFromLeader();
        }

        public async Task<string> GenerateClusterIdentityAsync(string id, string databaseName)
        {
            var (_, result) = await SendToLeaderAsync(new IncrementClusterIdentityCommand(databaseName)
            {
                Prefix = id.ToLower()
            });

            if (result == null)
            {
                throw new InvalidOperationException(
                    "Expected to get result from raft command that should generate a cluster-wide identity, but didn't.");
            }

            return id + result;
        }

        public DatabaseRecord LoadDatabaseRecord(string databaseName)
        {
            TransactionOperationContext context;
            using (ContextPool.AllocateOperationContext(out context))
            using (context.OpenReadTransaction())
            {
                return Cluster.ReadDatabase(context, databaseName);
            }
        }

        public DatabaseRecord LoadDatabaseRecord(string databaseName, out long etag)
        {
            TransactionOperationContext context;
            using (ContextPool.AllocateOperationContext(out context))
            using (context.OpenReadTransaction())
            {
                return Cluster.ReadDatabase(context, databaseName, out etag);
            }
        }

        private async Task<(long Etag, object Result)> SendToLeaderAsyncInternal(CommandBase cmd)
        {
            //I think it is reasonable to expect timeout twice of error retry
            var timeoutTask = TimeoutManager.WaitFor(Engine.OperationTimeout, _shutdownNotification.Token);

            while (true)
            {
                ServerShutdown.ThrowIfCancellationRequested();

                if (_engine.CurrentState == RachisConsensus.State.Leader)
                {
                    return await _engine.PutAsync(cmd);
                }

                var logChange = _engine.WaitForHeartbeat();

                var cachedLeaderTag = _engine.LeaderTag; // not actually working
                try
                {
                    if (cachedLeaderTag == null)
                    {
                        var completed = await Task.WhenAny(logChange, TimeoutManager.WaitFor(TimeSpan.FromMilliseconds(10000), ServerShutdown));

                        if (completed != logChange)
                            throw new TimeoutException("Could not send command to leader because there is no leader, and we timed out waiting for one");

                        continue;
                    }

                    return await SendToNodeAsync(cachedLeaderTag, cmd);
                }
                catch (Exception ex)
                {
                    if (Logger.IsInfoEnabled)
                        Logger.Info("Tried to send message to leader, retrying", ex);

                    if (_engine.LeaderTag == cachedLeaderTag)
                        throw; // if the leader changed, let's try again
                }

                if (await Task.WhenAny(logChange, timeoutTask) == timeoutTask)
                    ThrowTimeoutException();
            }
        }

        private static void ThrowTimeoutException()
        {
            throw new TimeoutException();
        }

        private async Task<(long Etag, object Result)> SendToNodeAsync(string engineLeaderTag, CommandBase cmd)
        {
            using (ContextPool.AllocateOperationContext(out TransactionOperationContext context))
            {
                var djv = cmd.ToJson(context);
                var cmdJson = context.ReadObject(djv, "raft/command");

                ClusterTopology clusterTopology;
                using (context.OpenReadTransaction())
                    clusterTopology = _engine.GetTopology(context);

                if (clusterTopology.Members.TryGetValue(engineLeaderTag, out string leaderUrl) == false)
                    throw new InvalidOperationException("Leader " + engineLeaderTag + " was not found in the topology members");

                var command = new PutRaftCommand(context, cmdJson);

                if (_clusterRequestExecutor == null 
                    || _clusterRequestExecutor.Url.Equals(leaderUrl, StringComparison.OrdinalIgnoreCase) == false 
                    || _clusterRequestExecutor.ApiKey?.Equals(clusterTopology.ApiKey) == false)
                {
                    _clusterRequestExecutor?.Dispose();
                    _clusterRequestExecutor = ClusterRequestExecutor.CreateForSingleNode(leaderUrl, clusterTopology.ApiKey);
                    _clusterRequestExecutor.DefaultTimeout = Engine.OperationTimeout;
                }

                await _clusterRequestExecutor.ExecuteAsync(command, context, ServerShutdown);

                return (command.Result.Etag, command.Result.Data);
            }
        }

        private class PutRaftCommand : RavenCommand<PutRaftCommandResult>
        {
            private readonly JsonOperationContext _context;
            private readonly BlittableJsonReaderObject _command;
            public override bool IsReadRequest => false;
            public long CommandIndex { get; private set; }

            public PutRaftCommand(JsonOperationContext context, BlittableJsonReaderObject command)
            {
                _context = context;
                _command = command;
            }

            public override HttpRequestMessage CreateRequest(ServerNode node, out string url)
            {
                url = $"{node.Url}/rachis/send";

                var request = new HttpRequestMessage
                {
                    Method = HttpMethod.Post,
                    Content = new BlittableJsonContent(stream =>
                    {
                        using (var writer = new BlittableJsonTextWriter(_context, stream))
                        {
                            writer.WriteObject(_command);
                        }
                    })
                };

                return request;
            }

            public override void SetResponse(BlittableJsonReaderObject response, bool fromCache)
            {
                Result = JsonDeserializationCluster.PutRaftCommandResult(response);
            }
        }

        public class PutRaftCommandResult
        {
            public long Etag { get; set; }

            public object Data { get; set; }
        }

        public Task WaitForTopology(Leader.TopologyModification state)
        {
            return _engine.WaitForTopology(state);
        }

        public Task WaitForState(RachisConsensus.State state)
        {
            return _engine.WaitForState(state);
        }

        public void ClusterAcceptNewConnection(Stream client)
        {
            _engine.AcceptNewConnection(client);
        }

        public async Task WaitForCommitIndexChange(RachisConsensus.CommitIndexModification modification, long value)
        {
            await _engine.WaitForCommitIndexChange(modification, value);
        }

        public string ClusterStatus()
        {
            return _engine.CurrentState + ", " + _engine.LastStateChangeReason;
        }
    }
}