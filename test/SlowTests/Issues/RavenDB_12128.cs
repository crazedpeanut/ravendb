﻿using System.Threading.Tasks;
using FastTests;
using Raven.Client.Documents;
using Raven.Client.Documents.Linq;
using Raven.Tests.Core.Utils.Entities;
using Tests.Infrastructure;
using Xunit;
using Xunit.Abstractions;

namespace SlowTests.Issues
{
    public class RavenDB_12128 : RavenTestBase
    {
        public RavenDB_12128(ITestOutputHelper output) : base(output)
        {
        }

        [RavenTheory(RavenTestCategory.Querying)]
        [RavenData(SearchEngineMode = RavenSearchEngineMode.All, DatabaseMode = RavenDatabaseMode.All)]
        public async Task CanQueryWithStartsWithAndCount(Options options)
        {
            using (var store = GetDocumentStore(options))
            {
                using (var session = store.OpenAsyncSession())
                {
                    await session.StoreAsync(new User { Name = "Arek" });

                    await session.SaveChangesAsync();

                    var count = await session.Query<User>().Where(i => i.Id.StartsWith("users/")).CountAsync();

                    Assert.Equal(1, count);

                    count = await session.Advanced.AsyncRawQuery<int>("from @all_docs where startsWith(id(), 'users/')").CountAsync();

                    Assert.Equal(1, count);
                }
            }
        }
    }
}
