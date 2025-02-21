//-----------------------------------------------------------------------
// <copyright file="IDocumentQuery.cs" company="Hibernating Rhinos LTD">
//     Copyright (c) Hibernating Rhinos LTD. All rights reserved.
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Raven.Client.Documents.Linq;
using Raven.Client.Documents.Queries;
using Raven.Client.Documents.Queries.Facets;
using Raven.Client.Documents.Queries.MoreLikeThis;
using Raven.Client.Documents.Queries.Suggestions;
using Raven.Client.Documents.Queries.TimeSeries;
using Raven.Client.Documents.Session.Querying.Sharding;

namespace Raven.Client.Documents.Session
{
    public interface IDocumentQueryBase<T>
    {
        /// <summary>
        ///     Register the query as a lazy-count query in the session and return a lazy
        ///     instance that will evaluate the query only when needed.
        /// </summary>
        Lazy<int> CountLazily();

        /// <summary>
        ///     Executed the query and returns the results.
        /// </summary>
        List<T> ToList();

        /// <summary>
        ///     Executed the query and returns the results.
        /// </summary>
        T[] ToArray();

        /// <summary>
        ///     Returns first element or throws if sequence is empty.
        /// </summary>
        T First();

        /// <summary>
        ///     Returns first element or default value for type if sequence is empty.
        /// </summary>
        T FirstOrDefault();

        /// <summary>
        ///     Returns first element or throws if sequence is empty or contains more than one element.
        /// </summary>
        T Single();

        /// <summary>
        ///     Returns first element or default value for given type if sequence is empty. Throws if sequence contains more than
        ///     one element.
        /// </summary>
        T SingleOrDefault();

        /// <summary>
        ///     Checks if the given query matches any records
        /// </summary>
        bool Any();

        /// <summary>
        /// Gets the total count of records for this query
        /// </summary>
        int Count();

        /// <summary>
        /// Gets the total count of records for this query as int64 
        /// </summary>
        long LongCount();

        /// <summary>
        ///     Register the query as a lazy query in the session and return a lazy
        ///     instance that will evaluate the query only when needed.
        ///     Also provide a function to execute when the value is evaluated
        /// </summary>
        Lazy<IEnumerable<T>> Lazily(Action<IEnumerable<T>> onEval = null);
    }

    /// <summary>
    ///     Allows to express a query directly in RQL using string containing query syntax.
    /// </summary>
    /// <typeparam name="T">Query result type</typeparam>
    /// <remarks><seealso ref="https://ravendb.net/docs/article-page/6.0/csharp/client-api/session/querying/how-to-query#session.advanced.rawquery"/></remarks>
    public interface IRawDocumentQuery<T> :
        IPagingDocumentQueryBase<T, IRawDocumentQuery<T>>,
        IQueryBase<T, IRawDocumentQuery<T>>,
        IDocumentQueryBase<T>
    {
        /// <summary>
        ///     Allows to change the projection behavior of a query. The projection behavior allows to control where RavenDB will try to retrieve the fields values from.
        /// </summary>
        /// <param name="projectionBehavior">Desired projection behavior type</param>
        /// <remarks><seealso ref="https://ravendb.net/docs/article-page/6.0/csharp/indexes/querying/projections#projection-behavior-with-a-static-index"/></remarks>
        IRawDocumentQuery<T> Projection(ProjectionBehavior projectionBehavior);
        
        /// <summary>
        ///     Execute raw query aggregated by facet.
        /// </summary>
        /// <returns>Dictionary with declared facet names keys and corresponding facet aggregation values</returns>
        /// <remarks><seealso ref="https://ravendb.net/docs/article-page/6.0/csharp/indexes/querying/faceted-search"/></remarks>
        Dictionary<string, FacetResult> ExecuteAggregation();
    }

    /// <inheritdoc cref="IDocumentQueryBase{T, TSelf} "/>
    public interface IDocumentQuery<T> :
        IDocumentQueryBase<T, IDocumentQuery<T>>,
        IDocumentQueryBase<T>
    {
        string IndexName { get; }

        /// <summary>
        ///     Whether we should apply distinct operation to the query on the server side
        /// </summary>
        bool IsDistinct { get; }

        IRavenQueryable<T> ToQueryable();

        /// <summary>
        ///     Returns the query result. Accessing this property for the first time will execute the query.
        /// </summary>
        QueryResult GetQueryResult();

        /// <summary>
        ///     Selects the specified fields directly from the index if the are stored. If the field is not stored in index, value
        ///     will come from document directly.
        /// </summary>
        /// <typeparam name="TProjection">Type of the projection.</typeparam>
        /// <param name="fields">Array of fields to load.</param>
        IDocumentQuery<TProjection> SelectFields<TProjection>(params string[] fields);

        /// <summary>
        ///     Selects the specified fields according to the given projection behavior.
        /// </summary>
        /// <typeparam name="TProjection">Type of the projection.</typeparam>
        /// <param name="projectionBehavior">Projection behavior to use.</param>
        /// <param name="fields">Array of fields to load.</param>
        IDocumentQuery<TProjection> SelectFields<TProjection>(ProjectionBehavior projectionBehavior, params string[] fields);

        /// <summary>
        ///     Selects the specified fields.
        /// </summary>
        /// <typeparam name="TProjection">Type of the projection.</typeparam>
        /// <param name="queryData">An object containing the fields to load, field projections and a From-Token alias name</param>
        IDocumentQuery<TProjection> SelectFields<TProjection>(QueryData queryData);

        /// <summary>
        ///     Selects the specified fields directly from the index if the are stored. If the field is not stored in index, value
        ///     will come from document directly.
        ///     <para>Array of fields will be taken from TProjection</para>
        /// </summary>
        /// <typeparam name="TProjection">Type of the projection from which fields will be taken.</typeparam>
        IDocumentQuery<TProjection> SelectFields<TProjection>();

        /// <summary>
        ///     Selects the specified fields according to the given projection behavior.
        ///     <para>Array of fields will be taken from TProjection</para>
        /// </summary>
        /// <typeparam name="TProjection">Type of the projection from which fields will be taken.</typeparam>
        /// <param name="projectionBehavior">Projection behavior to use.</param>
        IDocumentQuery<TProjection> SelectFields<TProjection>(ProjectionBehavior projectionBehavior);

        /// <summary>
        ///     Selects a Time Series Aggregation based on
        ///     a time series query generated by an ITimeSeriesQueryBuilder.
        /// </summary>
        IDocumentQuery<TTimeSeries> SelectTimeSeries<TTimeSeries>(Func<ITimeSeriesQueryBuilder, TTimeSeries> timeSeriesQuery);

        /// <summary>
        /// Changes the return type of the query
        /// </summary>
        IDocumentQuery<TResult> OfType<TResult>();

        /// <inheritdoc cref="IAbstractDocumentQuery{T}.GroupBy(string,string[])"/>
        IGroupByDocumentQuery<T> GroupBy(string fieldName, params string[] fieldNames);

        /// <inheritdoc cref="IAbstractDocumentQuery{T}.GroupBy(ValueTuple{string, GroupByMethod}, ValueTuple{string, GroupByMethod}[])" />
        IGroupByDocumentQuery<T> GroupBy((string Name, GroupByMethod Method) field, params (string Name, GroupByMethod Method)[] fields);

        /// <inheritdoc cref="IMoreLikeThisOperations{T}"/>
        /// <param name="builder">Configure MoreLikeThis query by builder. See more at: <see cref="IMoreLikeThisBuilderForDocumentQuery{T}"/> or see <see cref="IMoreLikeThisBuilderForAsyncDocumentQuery{T}">here</see> for async operations.</param>
        IDocumentQuery<T> MoreLikeThis(Action<IMoreLikeThisBuilderForDocumentQuery<T>> builder);
        
        /// <summary>
        /// Filter allows querying on documents without the need for issuing indexes. It is meant for exploratory queries or post query filtering. Criteria are evaluated at query time so please use Filter wisely to avoid performance issues.
        /// </summary>
        /// <param name="builder">Builder of a Filter query</param>
        /// <param name="limit">Limits the number of documents processed by Filter.</param>
        /// <returns></returns>
        IDocumentQuery<T> Filter(Action<IFilterFactory<T>> builder, int limit = int.MaxValue);
        
        IAggregationDocumentQuery<T> AggregateBy(Action<IFacetBuilder<T>> builder);

        IAggregationDocumentQuery<T> AggregateBy(FacetBase facet);

        IAggregationDocumentQuery<T> AggregateBy(IEnumerable<FacetBase> facets);

        IAggregationDocumentQuery<T> AggregateUsing(string facetSetupDocumentId);

        /// <inheritdoc cref="ISuggestionQuery{T}.AndSuggestUsing(SuggestionBase)"/>
        ISuggestionDocumentQuery<T> SuggestUsing(SuggestionBase suggestion);

        /// <inheritdoc cref="ISuggestionQuery{T}.AndSuggestUsing(Action{ISuggestionBuilder{T}})"/>
        ISuggestionDocumentQuery<T> SuggestUsing(Action<ISuggestionBuilder<T>> builder);

        /// <summary>
        /// It adds additional shard context to a query so it will be executed only on the relevant shards
        /// </summary>
        IDocumentQuery<T> ShardContext(Action<IQueryShardedContextBuilder> builder);
    }
}
