﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using FastTests;
using Raven.Client.Documents.Conventions;
using Raven.Client.Documents.Operations.Revisions;
using Raven.Client.Documents.Subscriptions;
using Raven.Client.Util;
using Raven.Tests.Core.Utils.Entities;
using Sparrow.Json;
using Tests.Infrastructure;
using Xunit;
using Xunit.Abstractions;

namespace SlowTests.Client.Subscriptions
{
    public class SubscriptionScriptErrorHandling : RavenTestBase
    {
        public SubscriptionScriptErrorHandling(ITestOutputHelper output) : base(output)
        {
        }

        private readonly TimeSpan _reasonableWaitTime = Debugger.IsAttached ? TimeSpan.FromMinutes(5) : TimeSpan.FromSeconds(15);

        [RavenTheory(RavenTestCategory.Subscriptions)]
        [RavenData(DatabaseMode = RavenDatabaseMode.Sharded, Skip = "https://issues.hibernatingrhinos.com/issue/RavenDB-21548")]
        [RavenData(DatabaseMode = RavenDatabaseMode.Single)]
        public void ValidateFailedSubscriptionScriptExceptionHandling(Options options)
        {
            using (var store = GetDocumentStore(options))
            {
                var subscriptionId = store.Subscriptions.Create(new SubscriptionCreationOptions()
                {
                    Query = @"
declare function project(d){
    throw 'a party';
    return d;
}
from Users as d
select project(d)
"
                });

                var subscription = store.Subscriptions.GetSubscriptionWorker<User>(new SubscriptionWorkerOptions(subscriptionId)
                {
                    TimeToWaitBeforeConnectionRetry = TimeSpan.FromSeconds(5)
                });

                var exceptions = new List<Exception>();

                var mre = new ManualResetEvent(false);
                var receivedItem = new SubscriptionBatch<User>.Item();
                var userId = string.Empty;

                using (var session = store.OpenSession())
                {
                    var newUser = new User();
                    session.Store(newUser);
                    session.SaveChanges();
                    userId = session.Advanced.GetDocumentId(newUser);
                }

                subscription.Run(x =>
                {
                    foreach (var item in x.Items)
                    {
                        receivedItem = item;
                        try
                        {
                            var res = item;
                        }
                        catch (Exception e)
                        {
                            exceptions.Add(e);
                        }
                    }
                    mre.Set();
                });

                Assert.True(mre.WaitOne(_reasonableWaitTime));
                Assert.NotNull(receivedItem);
                Assert.Throws<InvalidOperationException>(() => receivedItem.Result);
                Assert.NotNull(receivedItem.Metadata);
                Assert.Equal(receivedItem.Id, userId);
            }
        }

        [RavenTheory(RavenTestCategory.Subscriptions | RavenTestCategory.Revisions)]
        [RavenData(DatabaseMode = RavenDatabaseMode.Single)]
        public void ValidateFailedRevisionsSubscriptionScriptExceptionHandling(Options options)
        {
            using (var store = GetDocumentStore(options))
            {
                using (var context = JsonOperationContext.ShortTermSingleUse())
                {
                    var configuration = new RevisionsConfiguration
                    {
                        Default = new RevisionsCollectionConfiguration
                        {
                            Disabled = false,
                            MinimumRevisionsToKeep = 5,
                        },
                        Collections = new Dictionary<string, RevisionsCollectionConfiguration>
                        {
                            ["Users"] = new RevisionsCollectionConfiguration
                            {
                                Disabled = false
                            },
                            ["Dons"] = new RevisionsCollectionConfiguration
                            {
                                Disabled = false
                            }
                        }
                    };

                    AsyncHelpers.RunSync(() => Server.ServerStore.ModifyDatabaseRevisions(context,
                        store.Database,
                        DocumentConventions.Default.Serialization.DefaultConverter.ToBlittable(configuration,
                            context), Guid.NewGuid().ToString()));
                }

                var subscriptionId = store.Subscriptions.Create(new SubscriptionCreationOptions()
                {
                    Query = @"
declare function project(d){
    throw 'nice';
    return d;
}
from Users (Revisions = true) as d
select project(d)
"
                });

                var subscription = store.Subscriptions.GetSubscriptionWorker<User>(new SubscriptionWorkerOptions(subscriptionId)
                {
                    TimeToWaitBeforeConnectionRetry = TimeSpan.FromSeconds(5)
                });

                var exceptions = new List<Exception>();

                var mre = new ManualResetEvent(false);
                var receivedItem = new SubscriptionBatch<User>.Item();

                var userId = string.Empty;

                using (var session = store.OpenSession())
                {
                    var newUser = new User();
                    session.Store(newUser);
                    session.SaveChanges();
                    userId = session.Advanced.GetDocumentId(newUser);
                }

                subscription.Run(x =>
                {
                    foreach (var item in x.Items)
                    {
                        receivedItem = item;
                        try
                        {
                            var res = item;
                        }
                        catch (Exception e)
                        {
                            exceptions.Add(e);
                        }
                    }
                    mre.Set();
                });

                Assert.True(mre.WaitOne(_reasonableWaitTime));
                Assert.NotNull(receivedItem);
                Assert.Throws<InvalidOperationException>(() => receivedItem.Result);
                Assert.NotNull(receivedItem.Metadata);
                Assert.Equal(receivedItem.Id, userId);
            }
        }
    }
}
