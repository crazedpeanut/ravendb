﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using FastTests;
using Newtonsoft.Json;
using Raven.Client;
using Raven.Client.Documents;
using Raven.Client.Documents.Operations;
using Raven.Client.Documents.Operations.ETL;
using Raven.Client.ServerWide.Operations;
using Raven.Server.ServerWide.Context;
using Raven.Tests.Core.Utils.Entities;
using Tests.Infrastructure;
using Tests.Infrastructure.Entities;
using Xunit;
using Employee = Orders.Employee;
using Xunit.Abstractions;

namespace SlowTests.Server.Documents.ETL.Raven
{
    public class BasicRavenEtlTests : RavenTestBase
    {
        public BasicRavenEtlTests(ITestOutputHelper output) : base(output)
        {
        }

        [RavenTheory(RavenTestCategory.Etl)]
        [InlineData(RavenDatabaseMode.Single, RavenDatabaseMode.Single)]
        [InlineData(RavenDatabaseMode.Single, RavenDatabaseMode.Sharded)]
        [InlineData(RavenDatabaseMode.Sharded, RavenDatabaseMode.Single)]
        [InlineData(RavenDatabaseMode.Sharded, RavenDatabaseMode.Sharded)]
        public void Simple_script(RavenDatabaseMode srcDbMode, RavenDatabaseMode dstDbMode)
        {
            using (var src = GetDocumentStore(Options.ForMode(srcDbMode)))
            using (var dest = GetDocumentStore(Options.ForMode(dstDbMode)))
            {
                Etl.AddEtl(src, dest, "Users", script: @"this.Name = 'James Doe';
                                       loadToUsers(this);");

                var etlDone = Etl.WaitForEtlToComplete(src);

                using (var session = src.OpenSession())
                {
                    session.Store(new User()
                    {
                        Name = "Joe Doe"
                    });

                    session.SaveChanges();
                }

                etlDone.Wait(TimeSpan.FromMinutes(1));

                using (var session = dest.OpenSession())
                {
                    var user = session.Load<User>("users/1-A");

                    Assert.NotNull(user);
                    Assert.Equal("James Doe", user.Name);
                }

                etlDone.Reset();

                using (var session = src.OpenSession())
                {
                    session.Delete("users/1-A");

                    session.SaveChanges();
                }

                etlDone.Wait(TimeSpan.FromMinutes(1));

                using (var session = dest.OpenSession())
                {
                    var user = session.Load<User>("users/1-A");

                    Assert.Null(user);
                }
            }
        }

        [RavenTheory(RavenTestCategory.Etl)]
        [InlineData(RavenDatabaseMode.Single, RavenDatabaseMode.Single)]
        [InlineData(RavenDatabaseMode.Single, RavenDatabaseMode.Sharded)]
        [InlineData(RavenDatabaseMode.Sharded, RavenDatabaseMode.Single)]
        [InlineData(RavenDatabaseMode.Sharded, RavenDatabaseMode.Sharded)]
        public async Task WithDocumentPrefix(RavenDatabaseMode srcDbMode, RavenDatabaseMode dstDbMode)
        {
            using (var src = GetDocumentStore(Options.ForMode(srcDbMode)))
            using (var dest = GetDocumentStore(Options.ForMode(dstDbMode)))
            {
                Etl.AddEtl(src, new RavenEtlConfiguration()
                {
                    Name = "with prefix",
                    ConnectionStringName = "my-con",
                    Transforms =
                        {
                            new Transformation
                            {
                                Name = $"ETL : my-con2",
                                Collections = new List<string>{"Users"},
                                Script = "loadToUsers2(this);",
                                ApplyToAllDocuments = false,
                                Disabled = false,
                                DocumentIdPostfix = "chicago"
                            },
                            new Transformation
                            {
                                Name = $"ETL : my-con1",
                                Collections = new List<string>{"Users"},
                                Script = "loadToUsers(this); loadToUsers2(this);",
                                ApplyToAllDocuments = false,
                                Disabled = false,
                                DocumentIdPostfix = ",Chicago"
                            }
                        },
                },
                    new RavenConnectionString
                    {
                        Name = "my-con",
                        Database = dest.Database,
                        TopologyDiscoveryUrls = dest.Urls,
                    }
                );

                using (var session = src.OpenSession())
                {
                    session.Store(new User()
                    {
                        Name = "Joe Doe"
                    });

                    session.SaveChanges();
                }

                var timeout = 30_000;
                WaitForValue(() =>
                {
                    using (var session = dest.OpenSession())
                    {
                        var docs = session.Advanced.LoadStartingWith<User>("users/1-A");
                        return docs.Length;
                    }
                }, expectedVal: 2, timeout);

                string secondaryId;
                using (var session = dest.OpenSession())
                {
                    var user = session.Load<User>("users/1-A,Chicago");

                    Assert.NotNull(user);
                    Assert.Equal("Joe Doe", user.Name);
                    var other = session.Advanced.RawQuery<object>("from Users2 where startsWith(id(), 'users/1-A/')")
                        .Single();

                    secondaryId = session.Advanced.GetDocumentId(other);
                    Assert.StartsWith("users/1-A/users2/chicago/", secondaryId);
                }

                using (var session = src.OpenSession())
                {
                    session.Delete("users/1-A");
                    session.SaveChanges();
                }

                var etlReachedDestination = WaitForValue(() =>
                {
                    using (var session = dest.OpenSession())
                    {
                        return session.Advanced.Exists("users/1-A,Chicago") == false &&
                               session.Advanced.Exists(secondaryId) == false;
                    }
                }, true, timeout);

                Assert.True(etlReachedDestination, await Etl.GetEtlDebugInfo(src.Database, TimeSpan.FromMilliseconds(timeout), srcDbMode));

                using (var session = dest.OpenSession())
                {
                    var user = session.Load<User>("users/1-A,Chicago");

                    Assert.Null(user);

                    var item = session.Advanced.RawQuery<object>("from Users2 where startsWith(id(), 'users/1-A/')")
                        .SingleOrDefault();
                    Assert.Null(item);
                }
            }
        }

        [RavenTheory(RavenTestCategory.Etl)]
        [RavenData(DatabaseMode = RavenDatabaseMode.All)]
        public void SetMentorToEtlAndFailover(Options options)
        {
            using (var src = GetDocumentStore())
            using (var dest = GetDocumentStore(options))
            {
                Etl.AddEtl(src, dest, "Users", script: null, mentor: "C");

                var database = GetDatabase(src.Database).Result;

                Assert.Equal("C", database.EtlLoader.RavenDestinations[0].MentorNode);

                var etlDone = Etl.WaitForEtlToComplete(src);

                using (var session = src.OpenSession())
                {
                    session.Store(new User()
                    {
                        Name = "Joe Doe2"
                    });

                    session.SaveChanges();
                }

                etlDone.Wait(TimeSpan.FromMinutes(1));

                using (var session = dest.OpenSession())
                {
                    var user = session.Load<User>("users/1-A");

                    Assert.NotNull(user);
                    Assert.Equal("Joe Doe2", user.Name);
                }

                etlDone.Reset();

                using (var session = src.OpenSession())
                {
                    session.Delete("users/1-A");

                    session.SaveChanges();
                }

                etlDone.Wait(TimeSpan.FromMinutes(1));

                using (var session = dest.OpenSession())
                {
                    var user = session.Load<User>("users/1-A");

                    Assert.Null(user);
                }
            }
        }

        [RavenTheory(RavenTestCategory.Etl)]
        [InlineData(RavenDatabaseMode.Single, RavenDatabaseMode.Single)]
        [InlineData(RavenDatabaseMode.Single, RavenDatabaseMode.Sharded)]
        [InlineData(RavenDatabaseMode.Sharded, RavenDatabaseMode.Single)]
        [InlineData(RavenDatabaseMode.Sharded, RavenDatabaseMode.Sharded)]
        public void No_script(RavenDatabaseMode srcDbMode, RavenDatabaseMode dstDbMode)
        {
            using (var src = GetDocumentStore(Options.ForMode(srcDbMode)))
            using (var dest = GetDocumentStore(Options.ForMode(dstDbMode)))
            {
                Etl.AddEtl(src, dest, "Users", script: null);

                var etlDone = Etl.WaitForEtlToComplete(src);

                using (var session = src.OpenSession())
                {
                    session.Store(new User
                    {
                        Name = "Joe Doe"
                    });

                    session.SaveChanges();
                }

                etlDone.Wait(TimeSpan.FromMinutes(1));

                using (var session = dest.OpenSession())
                {
                    var user = session.Load<User>("users/1-A");

                    Assert.NotNull(user);
                    Assert.Equal("Joe Doe", user.Name);
                }

                etlDone.Reset();

                using (var session = src.OpenSession())
                {
                    session.Delete("users/1-A");

                    session.SaveChanges();
                }

                etlDone.Wait(TimeSpan.FromMinutes(1));

                using (var session = dest.OpenSession())
                {
                    var user = session.Load<User>("users/1-A");

                    Assert.Null(user);
                }
            }
        }

        [RavenTheory(RavenTestCategory.Etl)]
        [RavenData(DatabaseMode = RavenDatabaseMode.All)]
        public void Filtering_and_transformation_with_load_document(Options dstOptions)
        {
            using (var src = GetDocumentStore())
            using (var dest = GetDocumentStore(dstOptions))
            {
                var etlDone = Etl.WaitForEtlToComplete(src);

                Etl.AddEtl(src, dest, "users", @"
if (this.Age % 4 == 0) 
    return;
else if (this.Age % 2 == 0) 
    return;

loadToUsers(
    {
        Name: this.Name + ' ' + this.LastName, 
        Address: load(this.AddressId)
    });
");
                const int count = 30;

                using (var session = src.OpenSession())
                {
                    for (int i = 0; i < count; i++)
                    {
                        var userId = "users/" + i;
                        var addressId = $"addresses/{i}${userId}";

                        session.Store(new User
                        {
                            Age = i,
                            Name = "James",
                            LastName = "Smith",
                            AddressId = addressId
                        }, userId);

                        session.Store(new Address
                        {
                            City = "New York"
                        }, addressId);
                    }

                    session.SaveChanges();
                }

                etlDone.Wait(TimeSpan.FromSeconds(30));

                using (var session = dest.OpenSession())
                {
                    var loaded = 0;

                    for (int i = 0; i < count; i++)
                    {
                        var user = session.Load<UserWithAddress>("users/" + i);

                        if (i % 2 == 0)
                        {
                            Assert.Null(user);
                        }
                        else
                        {
                            Assert.Equal("New York", user.Address.City);
                            loaded++;
                        }
                    }

                    Assert.Equal(15, loaded);
                }

                etlDone.Reset();

                using (var session = src.OpenSession())
                {
                    for (var i = 0; i < count; i++)
                    {
                        session.Delete($"users/{i}");
                    }

                    session.SaveChanges();
                }

                etlDone.Wait(TimeSpan.FromMinutes(1));

                using (var session = dest.OpenSession())
                {
                    for (int i = 0; i < count; i++)
                    {
                        var user = session.Load<UserWithAddress>("users/" + i);

                        Assert.Null(user);
                    }
                }
            }
        }

        [RavenTheory(RavenTestCategory.Etl)]
        [RavenData(DatabaseMode = RavenDatabaseMode.All)]
        public void Loading_to_different_collections(Options dstOptions)
        {
            using (var src = GetDocumentStore())
            using (var dest = GetDocumentStore(dstOptions))
            {
                var etlDone = Etl.WaitForEtlToComplete(src);

                Etl.AddEtl(src, dest, "users", @"
loadToUsers(this);
loadToPeople({Name: this.Name + ' ' + this.LastName });
loadToAddresses(load(this.AddressId));
");
                const int count = 5;

                using (var session = src.OpenSession())
                {
                    for (int i = 1; i <= count; i++)
                    {
                        session.Store(new User
                        {
                            Age = i,
                            Name = "James",
                            LastName = "Smith",
                            AddressId = $"addresses/{i}-A"
                        });

                        session.Store(new Address
                        {
                            City = "New York"
                        });
                    }

                    session.SaveChanges();
                }

                etlDone.Wait(TimeSpan.FromSeconds(30));

                using (var session = dest.OpenSession())
                {
                    for (var i = 1; i <= count; i++)
                    {
                        var user = session.Load<User>($"users/{i}" + "-A");
                        Assert.NotNull(user);
                        Assert.Equal("James", user.Name);
                        Assert.Equal("Smith", user.LastName);

                        var metadata = session.Advanced.GetMetadataFor(user);
                        Assert.Equal("Users", metadata[Constants.Documents.Metadata.Collection]);

                        var person = session.Advanced.LoadStartingWith<Person>($"users/{i}-A/people/")[0];
                        Assert.NotNull(person);
                        Assert.Equal("James Smith", person.Name);

                        metadata = session.Advanced.GetMetadataFor(person);
                        Assert.Equal("People", metadata[Constants.Documents.Metadata.Collection]);

                        var address = session.Advanced.LoadStartingWith<Address>($"users/{i}-A/addresses/")[0];
                        Assert.NotNull(address);
                        Assert.Equal("New York", address.City);

                        metadata = session.Advanced.GetMetadataFor(address);
                        Assert.Equal("Addresses", metadata[Constants.Documents.Metadata.Collection]);
                    }
                }

                var docsCount = GetCountOfDocuments(dest);
                Assert.Equal(15, docsCount);

                etlDone.Reset();

                using (var session = src.OpenSession())
                {
                    session.Delete("users/3-A");

                    session.SaveChanges();
                }

                etlDone.Wait(TimeSpan.FromSeconds(30));

                using (var session = dest.OpenSession())
                {
                    var user = session.Load<User>("users/3-A");
                    Assert.Null(user);

                    var persons = session.Advanced.LoadStartingWith<Person>("users/3-A/people/");
                    Assert.Equal(0, persons.Length);

                    var addresses = session.Advanced.LoadStartingWith<Address>("users/3-A/addresses/");
                    Assert.Equal(0, addresses.Length);
                }

                docsCount = GetCountOfDocuments(dest);

                Assert.Equal(12, docsCount);
            }
        }

        [RavenTheory(RavenTestCategory.Etl)]
        [InlineData(RavenDatabaseMode.Single, RavenDatabaseMode.Single)]
        [InlineData(RavenDatabaseMode.Single, RavenDatabaseMode.Sharded)]
        [InlineData(RavenDatabaseMode.Sharded, RavenDatabaseMode.Single)]
        [InlineData(RavenDatabaseMode.Sharded, RavenDatabaseMode.Sharded)]
        public void Loading_to_different_collections_using_this(RavenDatabaseMode srcDbMode, RavenDatabaseMode dstDbMode)
        {
            using (var src = GetDocumentStore(Options.ForMode(srcDbMode)))
            using (var dest = GetDocumentStore(Options.ForMode(dstDbMode)))
            {
                var etlDone = Etl.WaitForEtlToComplete(src, numOfProcessesToWaitFor: 3);

                Etl.AddEtl(src, dest, "Employees", @"
loadToPeople(this);
loadToAddresses(this.Address);
");
                const int count = 5;

                using (var session = src.OpenSession())
                {
                    for (int i = 1; i <= count; i++)
                    {
                        session.Store(new Employee
                        {
                            FirstName = "James",
                            LastName = "Smith",
                            Address = new Orders.Address()
                            {
                                Country = "USA",
                                City = "New York"
                            }
                        });
                    }

                    session.SaveChanges();
                }

                etlDone.Wait(TimeSpan.FromSeconds(30));

                using (var session = dest.OpenSession())
                {
                    for (var i = 1; i <= count; i++)
                    {
                        var person = session.Advanced.LoadStartingWith<Employee>($"employees/{i}-A/people/")[0];
                        Assert.NotNull(person);
                        Assert.Equal("James", person.FirstName);

                        var metadata = session.Advanced.GetMetadataFor(person);
                        Assert.Equal("People", metadata[Constants.Documents.Metadata.Collection]);

                        var address = session.Advanced.LoadStartingWith<Address>($"employees/{i}-A/addresses/")[0];
                        Assert.NotNull(address);
                        Assert.Equal("New York", address.City);

                        metadata = session.Advanced.GetMetadataFor(address);
                        Assert.Equal("Addresses", metadata[Constants.Documents.Metadata.Collection]);
                    }
                }

                var docsCount = GetCountOfDocuments(dest);

                Assert.Equal(10, docsCount);

                etlDone = Etl.WaitForEtlToComplete(src);

                using (var session = src.OpenSession())
                {
                    session.Delete("employees/3-A");

                    session.SaveChanges();
                }

                etlDone.Wait(TimeSpan.FromSeconds(30));

                using (var session = dest.OpenSession())
                {
                    var persons = session.Advanced.LoadStartingWith<Employee>("employees/3-A/people/");
                    Assert.Equal(0, persons.Length);

                    var addresses = session.Advanced.LoadStartingWith<Address>("employees/3-A/addresses/");
                    Assert.Equal(0, addresses.Length);
                }

                docsCount = GetCountOfDocuments(dest);

                Assert.Equal(8, docsCount);
            }
        }

        [RavenTheory(RavenTestCategory.Etl)]
        [InlineData(RavenDatabaseMode.Single, RavenDatabaseMode.Single)]
        [InlineData(RavenDatabaseMode.Single, RavenDatabaseMode.Sharded)]
        [InlineData(RavenDatabaseMode.Sharded, RavenDatabaseMode.Single)]
        [InlineData(RavenDatabaseMode.Sharded, RavenDatabaseMode.Sharded)]
        public void Loading_to_the_same_collection_by_js_object_should_preserve_collection_metadata(RavenDatabaseMode srcDbMode, RavenDatabaseMode dstDbMode)
        {
            using (var src = GetDocumentStore(Options.ForMode(srcDbMode)))
            using (var dest = GetDocumentStore(Options.ForMode(dstDbMode)))
            {
                var etlDone = Etl.WaitForEtlToComplete(src);

                Etl.AddEtl(src, dest, "users", @"
loadToUsers({Name: this.Name + ' ' + this.LastName });
");
                using (var session = src.OpenSession())
                {
                    session.Store(new User
                    {
                        Name = "James",
                        LastName = "Smith",
                    });

                    session.Store(new Address
                    {
                        City = "New York"
                    });

                    session.SaveChanges();
                }

                etlDone.Wait(TimeSpan.FromSeconds(30));

                using (var session = dest.OpenSession())
                {
                    var user = session.Load<User>("users/1-A");
                    Assert.NotNull(user);
                    Assert.Equal("James Smith", user.Name);

                    var metadata = session.Advanced.GetMetadataFor(user);
                    Assert.Equal("Users", metadata[Constants.Documents.Metadata.Collection]);
                }
            }
        }

        [RavenTheory(RavenTestCategory.Etl)]
        [InlineData(RavenDatabaseMode.Single, RavenDatabaseMode.Single)]
        [InlineData(RavenDatabaseMode.Single, RavenDatabaseMode.Sharded)]
        [InlineData(RavenDatabaseMode.Sharded, RavenDatabaseMode.Single)]
        [InlineData(RavenDatabaseMode.Sharded, RavenDatabaseMode.Sharded)]
        public async Task Update_of_disassembled_document(RavenDatabaseMode srcDbMode, RavenDatabaseMode dstDbMode)
        {
            using (var src = GetDocumentStore(Options.ForMode(srcDbMode)))
            using (var dest = GetDocumentStore(Options.ForMode(dstDbMode)))
            {
                var etlDone = Etl.WaitForEtlToComplete(src);

                Etl.AddEtl(src, dest, "Orders", @"
var orderData = {
    OrderLinesCount: this.Lines.length,
    TotalCost: 0
};

for (var i = 0; i < this.Lines.length; i++) {
    var line = this.Lines[i];
    var cost = (line.Quantity * line.PricePerUnit) *  ( 1 - line.Discount);

    orderData.TotalCost += cost;

    loadToOrderLines({
        Quantity: line.Quantity,
        ProductName: line.ProductName,
        Cost: cost
    });
}

loadToOrders(orderData);
");

                using (var session = src.OpenSession())
                {
                    session.Store(new Order
                    {
                        Lines = new List<OrderLine>
                        {
                            new OrderLine
                            {
                                ProductName = "a",
                                PricePerUnit = 10,
                                Quantity = 1
                            },
                            new OrderLine
                            {
                                ProductName = "b",
                                PricePerUnit = 10,
                                Quantity = 2
                            }
                        }
                    });

                    session.SaveChanges();
                }

                var timeout = TimeSpan.FromSeconds(30);

                Assert.True(etlDone.Wait(timeout), await AddDebugInfo(src, dest, timeout, srcDbMode));

                using (var session = dest.OpenSession())
                {
                    var order = session.Load<OrderWithLinesCount>("orders/1-A");

                    Assert.Equal(2, order.OrderLinesCount);
                    Assert.Equal(30, order.TotalCost);

                    var lines = session.Advanced.LoadStartingWith<LineItemWithTotalCost>("orders/1-A/OrderLines/").OrderBy(x => x.ProductName).ToList();

                    Assert.Equal(2, lines.Count);

                    Assert.Equal(10, lines[0].Cost);
                    Assert.Equal("a", lines[0].ProductName);
                    Assert.Equal(1, lines[0].Quantity);

                    Assert.Equal(20, lines[1].Cost);
                    Assert.Equal("b", lines[1].ProductName);
                    Assert.Equal(2, lines[1].Quantity);
                }

                etlDone.Reset();

                using (var session = src.OpenSession())
                {
                    session.Store(new Order
                    {
                        Lines = new List<OrderLine>
                        {
                            new OrderLine
                            {
                                ProductName = "a",
                                PricePerUnit = 10,
                                Quantity = 1
                            },
                            new OrderLine
                            {
                                ProductName = "b",
                                PricePerUnit = 10,
                                Quantity = 1
                            }
                        }
                    }, "orders/1-A");

                    session.SaveChanges();
                }

                etlDone.Wait(timeout);

                using (var session = dest.OpenSession())
                {
                    var order = session.Load<OrderWithLinesCount>("orders/1-A");

                    Assert.Equal(2, order.OrderLinesCount);
                    Assert.Equal(20, order.TotalCost);
                    var lines = session.Advanced.LoadStartingWith<LineItemWithTotalCost>("orders/1-A/OrderLines/").OrderBy(x => x.ProductName).ToList();
                    Assert.Equal(2, lines.Count);

                    Assert.Equal(10, lines[0].Cost);
                    Assert.Equal("a", lines[0].ProductName);
                    Assert.Equal(1, lines[0].Quantity);

                    Assert.Equal(10, lines[1].Cost);
                    Assert.Equal("b", lines[1].ProductName);
                    Assert.Equal(1, lines[1].Quantity);
                }
            }
        }

        [RavenTheory(RavenTestCategory.Etl)]
        [InlineData(RavenDatabaseMode.Single, RavenDatabaseMode.Single)]
        [InlineData(RavenDatabaseMode.Single, RavenDatabaseMode.Sharded)]
        [InlineData(RavenDatabaseMode.Sharded, RavenDatabaseMode.Single)]
        [InlineData(RavenDatabaseMode.Sharded, RavenDatabaseMode.Sharded)]
        public void Can_get_document_id(RavenDatabaseMode srcDbMode, RavenDatabaseMode dstDbMode)
        {
            using (var src = GetDocumentStore(Options.ForMode(srcDbMode)))
            using (var dest = GetDocumentStore(Options.ForMode(dstDbMode)))
            {
                Etl.AddEtl(src, dest, "Users", "this.Name = id(this); loadToUsers(this);");

                var etlDone = Etl.WaitForEtlToComplete(src);

                using (var session = src.OpenSession())
                {
                    session.Store(new User()
                    {
                        Name = "Joe Doe"
                    });

                    session.SaveChanges();
                }

                etlDone.Wait(TimeSpan.FromMinutes(1));

                using (var session = dest.OpenSession())
                {
                    var user = session.Load<User>("users/1-A");

                    Assert.NotNull(user);
                    Assert.Equal("users/1-A", user.Name);
                }
            }
        }

        [RavenFact(RavenTestCategory.Etl)]
        public void Can_put_space_after_loadTo_method_in_script()
        {
            var config = new RavenEtlConfiguration
            {
                Name = "test",
                ConnectionStringName = "test",
                Transforms =
                {
                    new Transformation
                    {
                        Name = "test",
                        Collections = {"Users"},
                        Script = @"loadToUsers (this);"
                    }
                }
            };

            config.Initialize(new RavenConnectionString
            {
                Database = "Foo",
                TopologyDiscoveryUrls = new[] { "http://localhost:8080" }
            });

            config.Validate(out List<string> errors);

            Assert.Equal(0, errors.Count);

            var collections = config.Transforms[0].GetCollectionsFromScript();

            Assert.Equal(1, collections.Length);
            Assert.Equal("Users", collections[0]);
        }

        [RavenFact(RavenTestCategory.Etl)]
        public void Error_if_script_does_not_contain_any_loadTo_method()
        {
            var config = new RavenEtlConfiguration
            {
                Name = "test",
                ConnectionStringName = "test",
                Transforms =
                {
                    new Transformation
                    {
                        Name = "test",
                        Collections = {"Users"},
                        Script = @"this.Name = 'aaa';"
                    }
                }
            };

            config.Initialize(new RavenConnectionString
            {
                Database = "Foo",
                TopologyDiscoveryUrls = new[] { "http://localhost:8080" }
            });

            config.Validate(out List<string> errors);

            Assert.Equal(1, errors.Count);

            Assert.Equal("No `loadTo<CollectionName>()` method call found in 'test' script", errors[0]);
        }

        [RavenTheory(RavenTestCategory.Etl)]
        [InlineData(RavenDatabaseMode.Single, RavenDatabaseMode.Single)]
        [InlineData(RavenDatabaseMode.Single, RavenDatabaseMode.Sharded)]
        [InlineData(RavenDatabaseMode.Sharded, RavenDatabaseMode.Single)]
        [InlineData(RavenDatabaseMode.Sharded, RavenDatabaseMode.Sharded)]
        public void Can_load_to_specific_collection_when_applying_to_all_docs(RavenDatabaseMode srcDbMode, RavenDatabaseMode dstDbMode)
        {
            using (var src = GetDocumentStore(Options.ForMode(srcDbMode)))
            using (var dest = GetDocumentStore(Options.ForMode(dstDbMode)))
            {
                var etlDone = Etl.WaitForEtlToComplete(src);

                Etl.AddEtl(src, dest, new string[0], script: @"
loadToUsers(this);
", applyToAllDocuments: true);

                using (var session = src.OpenSession())
                {
                    session.Store(new User
                    {
                        Name = "James",
                        LastName = "Smith"
                    }, "users/1");

                    session.SaveChanges();
                }

                etlDone.Wait(TimeSpan.FromSeconds(30));

                using (var session = dest.OpenSession())
                {
                    Assert.NotNull(session.Load<User>("users/1"));
                }
            }
        }

        private long GetCountOfDocuments(IDocumentStore store)
        {
            var record = store.Maintenance.Server.Send(new GetDatabaseRecordOperation(store.Database));
            if (record.IsSharded == false)
            {
                var stats = store.Maintenance.Send(new GetStatisticsOperation());
                return stats.CountOfDocuments;
            }

            var docsCount = 0L;
            var dbs = Server.ServerStore.DatabasesLandlord.TryGetOrCreateShardedResourcesStore(store.Database).ToList();
            foreach (var task in dbs)
            {
                var db = task.Result;
                using (db.DocumentsStorage.ContextPool.AllocateOperationContext(out DocumentsOperationContext context))
                using (context.OpenReadTransaction())
                {
                    docsCount += db.DocumentsStorage.GetNumberOfDocuments();
                }
            }

            return docsCount;
        }

        private async Task<string> AddDebugInfo(IDocumentStore src, IDocumentStore dst, TimeSpan timeout, RavenDatabaseMode srcDbMode)
        {
            var sb = new StringBuilder()
                .AppendLine($"ETL from '{src.Database}' failed to reach destination '{dst.Database}':");

            var etlInfo = await Etl.GetEtlDebugInfo(src.Database, timeout, srcDbMode);
            sb.AppendLine(etlInfo).AppendLine();

            var record = await dst.Maintenance.Server.SendAsync(new GetDatabaseRecordOperation(dst.Database));

            sb.AppendLine("destination database record:")
                .AppendLine(JsonConvert.SerializeObject(record));

            return sb.ToString();
        }

        private class UserWithAddress : User
        {
            public Address Address { get; set; }
        }

        private class OrderWithLinesCount
        {
            public int OrderLinesCount { get; set; }

            public decimal TotalCost { get; set; }
        }

        private class LineItemWithTotalCost
        {
            public string ProductName { get; set; }
            public decimal Cost { get; set; }
            public int Quantity { get; set; }
        }
    }
}
