//-----------------------------------------------------------------------
// <copyright file="CanProjectIdFromDocumentInQueries.cs" company="Hibernating Rhinos LTD">
//     Copyright (c) Hibernating Rhinos LTD. All rights reserved.
// </copyright>
//-----------------------------------------------------------------------

using System.Linq;
using FastTests;
using Raven.Client.Indexes;
using Raven.Client.Operations.Databases.Indexes;
using Xunit;

namespace SlowTests.Bugs
{
    public class CanProjectIdFromDocumentInQueries : RavenNewTestBase
    {
        [Fact]
        public void SelectIdFromDocumentWithIndexedQuery()
        {
            using (var store = GetDocumentStore())
            {
                var indexDefinition = new IndexDefinitionBuilder<Shipment>()
                {
                    Map = docs => from doc in docs
                        select new
                        {
                            doc.Id
                        }
                }.ToIndexDefinition(store.Conventions);
                indexDefinition.Name = "AmazingIndex";
                store.Admin.Send(new PutIndexesOperation(new[] {indexDefinition}));
              

                using (var session = store.OpenSession())
                {
                    session.Store(new Shipment()
                    {
                        Id = "shipment1",
                        Name = "Some shipment"
                    });
                    session.SaveChanges();

                    var shipment = session.Query<Shipment>("AmazingIndex")
                        .Customize(x=>x.WaitForNonStaleResults())
                        .Select(x => new Shipment
                        {
                            Id = x.Id,
                            Name = x.Name
                        }).Take(1).SingleOrDefault();

                    Assert.NotNull(shipment.Id);
                }
            }
        }

        [Fact]
        public void SelectIdFromDocumentWithDynamicQuery()
        {
            using (var store = GetDocumentStore())
            {
                using (var session = store.OpenSession())
                {
                    session.Store(new Shipment()
                    {
                        Id = "shipment1",
                        Name = "Some shipment"
                    });
                    session.SaveChanges();

                    var shipment = session.Query<Shipment>()
                        .Customize(x => x.WaitForNonStaleResults())
                        .Select(x => new Shipment()
                        {
                            Id = x.Id,
                            Name = x.Name
                        }).SingleOrDefault();

                    Assert.NotNull(shipment.Id);
                }
            }
        }

        private class Shipment
        {
            public string Id { get; set; }
            public string Name { get; set; }
        }
    }
}
