﻿using System;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FastTests;
using Raven.Client.Extensions;
using Raven.Server.Documents;
using Raven.Server.ServerWide.Context;
using Raven.Server.Smuggler.Documents;
using Raven.Server.Smuggler.Documents.Data;
using Xunit;
using Xunit.Abstractions;

namespace SlowTests.Issues
{
    public class RavenDB_5998 : RavenLowLevelTestBase
    {
        public RavenDB_5998(ITestOutputHelper output) : base(output)
        {
        }

        [Theory]
        [InlineData("SlowTests.Smuggler.Data.Northwind_3.5.35168.ravendbdump")]
        public async Task CanImportNorthwind(string file)
        {
            using (var cts = new CancellationTokenSource(TimeSpan.FromMinutes(5)))
            await using (var inputStream = GetType().Assembly.GetManifestResourceStream(file))
            await using (var stream = new GZipStream(inputStream, CompressionMode.Decompress))
            {
                Assert.NotNull(stream);

                using (DocumentDatabase database = CreateDocumentDatabase())
                using (database.DocumentsStorage.ContextPool.AllocateOperationContext(out DocumentsOperationContext context))
                using (var source = new StreamSource(stream, context, database.Name))
                {
                    var destination = database.Smuggler.CreateDestination();

                    var smuggler = database.Smuggler.Create(source, destination, context, new DatabaseSmugglerOptionsServerSide
                    {
                        TransformScript = "this['Test'] = 'NewValue';"
                    });

                    var result = await smuggler.ExecuteAsync().WithCancellation(cts.Token);

                    Assert.Equal(1059, result.Documents.ReadCount);
                    Assert.Equal(0, result.Documents.SkippedCount);
                    Assert.Equal(0, result.Documents.ErroredCount);

                    Assert.Equal(4, result.Indexes.ReadCount);
                    Assert.Equal(0, result.Indexes.ErroredCount);

                    Assert.Equal(0, result.RevisionDocuments.ReadCount);
                    Assert.Equal(0, result.RevisionDocuments.ErroredCount);

                    using (context.OpenReadTransaction())
                    {
                        var countOfDocuments = database.DocumentsStorage.GetNumberOfDocuments(context);
                        var countOfIndexes = database.IndexStore.GetIndexes().Count();

                        Assert.Equal(1059, countOfDocuments);
                        Assert.Equal(3, countOfIndexes);// there are 4 in ravendbdump, but Raven/DocumentsByEntityName is skipped

                        var doc = database.DocumentsStorage.Get(context, "orders/1");
                        string test;
                        Assert.True(doc.Data.TryGet("Test", out test));
                        Assert.Equal("NewValue", test);
                    }
                }
            }
        }
    }
}
