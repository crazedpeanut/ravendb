﻿using System.Net.Http;
using Raven.Client.Http;
using Raven.Client.Json;
using Sparrow.Json;

namespace Raven.Server.Documents.Commands.ETL
{
    public class QueueEtlTestCommand : RavenCommand
    {
        private readonly BlittableJsonReaderObject _testScript;
        public override bool IsReadRequest => true;

        public QueueEtlTestCommand(BlittableJsonReaderObject testScript)
        {
            _testScript = testScript;
        }

        public override HttpRequestMessage CreateRequest(JsonOperationContext ctx, ServerNode node, out string url)
        {
            url = $"{node.Url}/databases/{node.Database}/admin/etl/queue/test";

            var request = new HttpRequestMessage
            {
                Method = HttpMethod.Post,
                Content = new BlittableJsonContent(async stream =>
                {
                    await using (var writer = new AsyncBlittableJsonTextWriter(ctx, stream))
                    {
                        writer.WriteObject(_testScript);
                    }
                })
            };

            return request;
        }
    }
}
