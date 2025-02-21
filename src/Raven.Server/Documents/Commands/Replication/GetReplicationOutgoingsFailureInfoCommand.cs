﻿using System.Net.Http;
using Raven.Client.Http;
using Raven.Server.Documents.Handlers.Processors.Replication;
using Raven.Server.Json;
using Sparrow.Json;

namespace Raven.Server.Documents.Commands.Replication
{
    internal sealed class GetReplicationOutgoingsFailureInfoCommand : RavenCommand<ReplicationOutgoingsFailurePreview>
    {
        public GetReplicationOutgoingsFailureInfoCommand()
        {
        }

        public GetReplicationOutgoingsFailureInfoCommand(string nodeTag)
        {
            SelectedNodeTag = nodeTag;
        }

        public override HttpRequestMessage CreateRequest(JsonOperationContext ctx, ServerNode node, out string url)
        {
            url = $"{node.Url}/databases/{node.Database}/replication/debug/outgoing-failures";

            var request = new HttpRequestMessage
            {
                Method = HttpMethod.Get
            };

            return request;
        }

        public override void SetResponse(JsonOperationContext context, BlittableJsonReaderObject response, bool fromCache)
        {
            if (response == null)
                ThrowInvalidResponse();

            Result = JsonDeserializationServer.ReplicationOutgoingsFailurePreview(response);
        }

        public override bool IsReadRequest => true;
    }
}
