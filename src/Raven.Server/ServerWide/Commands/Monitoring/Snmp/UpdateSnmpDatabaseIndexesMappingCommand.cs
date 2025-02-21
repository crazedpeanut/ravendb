﻿using System.Collections.Generic;
using Raven.Client.ServerWide;
using Raven.Server.ServerWide.Context;
using Sparrow.Json;
using Sparrow.Json.Parsing;

namespace Raven.Server.ServerWide.Commands.Monitoring.Snmp
{
    public sealed class UpdateSnmpDatabaseIndexesMappingCommand : UpdateValueForDatabaseCommand
    {
        public static string GetStorageKey(string databaseName)
        {
            return $"{Helpers.ClusterStateMachineValuesPrefix(databaseName)}/monitoring/snmp/indexes/mapping";
        }

        private string _itemId;

        public List<string> Indexes { get; set; }

        public UpdateSnmpDatabaseIndexesMappingCommand()
        {
            // for deserialization
        }

        public UpdateSnmpDatabaseIndexesMappingCommand(string databaseName, List<string> indexes, string uniqueRequestId)
            : base(databaseName, uniqueRequestId)
        {
            Indexes = indexes;
        }

        public override string GetItemId()
        {
            return _itemId ?? (_itemId = GetStorageKey(DatabaseName));
        }

        public override void FillJson(DynamicJsonValue json)
        {
            if (Indexes == null)
                return;

            json[nameof(Indexes)] = new DynamicJsonArray(Indexes);
        }

        protected override UpdatedValue GetUpdatedValue(long index, RawDatabaseRecord record, ClusterOperationContext context, BlittableJsonReaderObject previousValue)
        {
            if (previousValue != null)
            {
                if (previousValue.Modifications == null)
                    previousValue.Modifications = new DynamicJsonValue();

                AddIndexesIfNecessary(previousValue.Modifications, previousValue, Indexes);

                if (previousValue.Modifications.Properties.Count == 0)
                {
                    return new UpdatedValue(UpdatedValueActionType.Noop, value: null);
                }

                return new UpdatedValue(UpdatedValueActionType.Update, context.ReadObject(previousValue, GetItemId()));
            }

            var djv = new DynamicJsonValue();
            AddIndexesIfNecessary(djv, null, Indexes);

            return new UpdatedValue(UpdatedValueActionType.Update, context.ReadObject(djv, GetItemId()));
        }

        private static void AddIndexesIfNecessary(DynamicJsonValue djv, BlittableJsonReaderObject previousValue, List<string> indexes)
        {
            if (indexes == null)
                return;

            var propertiesCount = previousValue?.Count ?? 0;
            foreach (var index in indexes)
            {
                if (previousValue == null || previousValue.TryGet(index, out long _) == false)
                    djv[index] = propertiesCount + djv.Properties.Count + 1;
            }
        }
    }
}
