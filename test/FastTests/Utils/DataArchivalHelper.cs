﻿using System;
using System.Threading.Tasks;
using Raven.Client.Documents;
using Raven.Client.Documents.Operations.DataArchival;

namespace FastTests.Utils
{
    public static class DataArchivalHelper
    {
        public static async Task SetupDataArchival(IDocumentStore store, Raven.Server.ServerWide.ServerStore serverStore, DataArchivalConfiguration configuration)
        {
            if (store == null)
                throw new ArgumentNullException(nameof(store));
            if (serverStore == null)
                throw new ArgumentNullException(nameof(serverStore));
            if (configuration == null)
                throw new ArgumentNullException(nameof(configuration));

            var result = await store.Maintenance.SendAsync(new ConfigureDataArchivalOperation(configuration));

            var documentDatabase = await serverStore.DatabasesLandlord.TryGetOrCreateResourceStore(store.Database);
            await documentDatabase.RachisLogIndexNotifications.WaitForIndexNotification(result.RaftCommandIndex.Value, serverStore.Engine.OperationTimeout);
        }
    }
}
