﻿// -----------------------------------------------------------------------
//  <copyright file="Samina3.cs" company="Hibernating Rhinos LTD">
//      Copyright (c) Hibernating Rhinos LTD. All rights reserved.
//  </copyright>
// -----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using FastTests;
using Raven.Client.Documents;
using Raven.Client.Documents.Indexes;
using Raven.Client.Documents.Linq;
using Raven.Client.Documents.Queries.Facets;
using Raven.Client.Documents.Session;
using Tests.Infrastructure;
using Xunit;
using Xunit.Abstractions;

namespace SlowTests.MailingList
{
    public class Samina3 : RavenTestBase
    {
        public Samina3(ITestOutputHelper output) : base(output)
        {
        }


        [RavenTheory(RavenTestCategory.Facets)]
        [RavenData(SearchEngineMode = RavenSearchEngineMode.All, DatabaseMode = RavenDatabaseMode.All)]
        public void Querying_a_sub_collection_in_an_index(Options options)
        {
            DateTime startDate1 = DateTime.Now;
            DateTime endDate1 = DateTime.Now.AddDays(10);
            DateTime startDate2 = DateTime.Now.AddDays(15);
            DateTime endDate2 = DateTime.Now.AddDays(20);

            using (var store = GetDocumentStore(options))
            {
                using (var session = store.OpenSession())
                {
                    var model = new SearchingViewModel() { Id = Guid.NewGuid().ToString(), UserFriendlyId = "p001", Feature = "Pool" };
                    model.BookingRequests.Add(new BookingRequest() { StartDay = startDate1, EndDay = endDate1 });
                    model.BookingRequests.Add(new BookingRequest() { StartDay = startDate2, EndDay = endDate2 });

                    session.Store(model);
                    session.SaveChanges();
                }

                new PropertiesSearchIndex().Execute(store);

                using (var session = store.OpenSession())
                {

                    var facets = new List<Facet>
                    {
                        new Facet()
                        {
                            FieldName = "Feature"
                        }
                    };

                    session.Store(new FacetSetup { Id = "facets/PropertySearchingFacets", Facets = facets });
                    session.SaveChanges();

                    QueryStatistics stats;
                    var query = session.Query<SearchingViewModel, PropertiesSearchIndex>()
                        .Statistics(out stats)
                        .Customize(x => x.WaitForNonStaleResults())
                        .Where(x => x.BookingRequests.Any(y => y.StartDay >= startDate1 && y.EndDay <= endDate2));

                    var result = query.ToList();

                    var facetResults = query
                        .AggregateUsing("facets/PropertySearchingFacets")
                        .Execute();

                    var facetedCount = facetResults["Feature"];

                    Assert.Equal(1, result.Count());
                    Assert.Equal(1, facetResults["Feature"].Values.First(x => x.Range == "pool").Count);
                    Assert.Equal("PropertiesSearchIndex", stats.IndexName);
                }
            }
        }

        private class SearchingViewModel
        {

            public string Id { get; set; }
            public string UserFriendlyId { get; set; }
            public string Feature { get; set; }

            public List<BookingRequest> BookingRequests { get; set; }

            public SearchingViewModel()
            {
                BookingRequests = new List<BookingRequest>();
            }
        }

        private class BookingRequest
        {
            public DateTime StartDay { get; set; }
            public DateTime EndDay { get; set; }
        }

        private class PropertiesSearchIndex : AbstractIndexCreationTask<SearchingViewModel>
        {
            public PropertiesSearchIndex()
            {
                Map = items =>
                      from searchingViewModel in items
                      select
                        new
                        {
                            Feature = searchingViewModel.Feature,
                            BookingRequests_StartDay = searchingViewModel.BookingRequests.Select(x => x.StartDay),
                            BookingRequests_EndDay = searchingViewModel.BookingRequests.Select(x => x.EndDay)
                        };
            }
        }
    }
}
