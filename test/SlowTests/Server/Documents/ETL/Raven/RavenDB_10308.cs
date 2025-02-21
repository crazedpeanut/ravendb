﻿using System;
using System.IO;
using FastTests;
using Raven.Client;
using Raven.Client.Extensions.Streams;
using Raven.Tests.Core.Utils.Entities;
using Tests.Infrastructure;
using Xunit;
using Xunit.Abstractions;

namespace SlowTests.Server.Documents.ETL.Raven
{
    public class RavenDB_10308 : RavenTestBase
    {
        public RavenDB_10308(ITestOutputHelper output) : base(output)
        {
        }

        [RavenTheory(RavenTestCategory.Etl)]
        [RavenData(DatabaseMode = RavenDatabaseMode.All)]
        public void Should_load_all_attachments_when_no_script_is_defined(Options options)
        {
            using (var src = GetDocumentStore(options))
            using (var dest = GetDocumentStore())
            {
                Etl.AddEtl(src, dest, "Users", script: null);

                var etlDone = Etl.WaitForEtlToComplete(src);

                using (var session = src.OpenSession())
                {
                    session.Store(new User()
                    {
                        Name = "Joe Doe"
                    }, "users/1");

                    session.Advanced.Attachments.Store("users/1", "photo.jpg", new MemoryStream(new byte[] { 1 }));

                    session.SaveChanges();
                }

                etlDone.Wait(TimeSpan.FromMinutes(1));

                using (var session = dest.OpenSession())
                {
                    var user = session.Load<User>("users/1");

                    Assert.NotNull(user);
                    Assert.Equal("Joe Doe", user.Name);

                    using (var attachment = session.Advanced.Attachments.Get("users/1", "photo.jpg"))
                    {
                        Assert.NotNull(attachment);
                        Assert.Equal("photo.jpg", attachment.Details.Name);
                        Assert.Equal(new byte[] { 1 }, attachment.Stream.ReadData());
                    }
                }

                etlDone.Reset();

                using (var session = src.OpenSession())
                {
                    session.Delete("users/1");

                    session.SaveChanges();
                }

                etlDone.Wait(TimeSpan.FromMinutes(1));

                using (var session = dest.OpenSession())
                {
                    var user = session.Load<User>("users/1");

                    Assert.Null(user);

                    using (var attachment = session.Advanced.Attachments.Get("users/1", "photo.jpg"))
                    {
                        Assert.Null(attachment);
                    }
                }
            }
        }

        [RavenTheory(RavenTestCategory.Etl)]
        [RavenData(DatabaseMode = RavenDatabaseMode.All)]
        public void Should_not_send_attachments_metadata_when_using_script(Options options)
        {
            using (var src = GetDocumentStore(options))
            using (var dest = GetDocumentStore())
            {
                Etl.AddEtl(src, dest, "Users", script: @"this.Name = 'James Doe';
                                       loadToUsers(this);");

                var etlDone = Etl.WaitForEtlToComplete(src);

                using (var session = src.OpenSession())
                {
                    session.Store(new User()
                    {
                        Name = "Joe Doe"
                    }, "users/1");

                    session.Advanced.Attachments.Store("users/1", "photo.jpg", new MemoryStream(new byte[] { 1 }));

                    session.SaveChanges();
                }

                etlDone.Wait(TimeSpan.FromMinutes(1));

                using (var session = dest.OpenSession())
                {
                    var user = session.Load<User>("users/1");

                    Assert.NotNull(user);
                    Assert.Equal("James Doe", user.Name);

                    var metadata = session.Advanced.GetMetadataFor(user);

                    Assert.False(metadata.ContainsKey(Constants.Documents.Metadata.Attachments));
                }
            }
        }
    }
}
