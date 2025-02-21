﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;
using Corax.Mappings;
using Corax.Querying.Matches;
using Corax.Querying.Matches.Meta;
using Corax.Querying.Matches.TermProviders;
using Corax.Utils;
using Voron;

namespace Corax.Querying;

public partial class IndexSearcher
{
    private static readonly Comparison<TermQueryItem> CompareTermQueryItemKeys = (item, queryItem) => item.Item.Compare(queryItem.Item);
    /// <summary>
    /// Test API only
    /// </summary>
    public MultiTermMatch InQuery(string field, List<string> inTerms) => InQuery(FieldMetadataBuilder(field), inTerms, default);
    public MultiTermMatch InQuery(in FieldMetadata field, List<string> inTerms) => InQuery(field, inTerms, default);

    public MultiTermMatch InQuery(in FieldMetadata field, List<(string Term, bool Exact)> inTerms, in CancellationToken token = default) => InQuery<(string Term, bool Exact)>(in field, inTerms, token);
    
    private MultiTermMatch InQuery<TTermType>(in FieldMetadata field, List<TTermType> inTerms, CancellationToken token)
    {
        if (typeof(TTermType) != typeof((string Term, bool Exact)) && typeof(TTermType) != typeof(Slice) && typeof(TTermType) != typeof(string))
            throw new NotSupportedException($"Type {typeof(TTermType)} is not supported.");
        
        var exactField = field.ChangeAnalyzer(FieldIndexingMode.Exact);
        
        var terms = _fieldsTree?.CompactTreeFor(field.FieldName);
        if (terms == null)
        {
            // If either the term or the field does not exist the request will be empty. 
            return MultiTermMatch.CreateEmpty(_transaction.Allocator);
        }

        if (inTerms.Count is > 1 and <= 4)
        {
            var stack = new BinaryMatch[inTerms.Count / 2];
            for (int i = 0; i < inTerms.Count / 2; i++)
            {
                if (typeof(TTermType) == typeof((string Term, bool Exact)))
                {
                    var first = ((string Term, bool Exact))(object)(inTerms[i * 2]);
                    var second = ((string Term, bool Exact))(object)(inTerms[i * 2 + 1]);
                    stack[i] = Or(
                        TermQuery(first.Exact ? exactField : field, first.Term, terms), 
                        TermQuery(second.Exact ? exactField : field, second.Term), token);
                    
                }
                else if (typeof(TTermType) == typeof(string))
                    stack[i] = Or(TermQuery(field, (string)(object)inTerms[i * 2], terms), TermQuery(field, (string)(object)inTerms[i * 2 + 1], terms), token);
                else 
                    stack[i] = Or(TermQuery(field, (Slice)(object)inTerms[i * 2], terms), TermQuery(field, (Slice)(object)inTerms[i * 2 + 1], terms), token);
                    
            }

            if (inTerms.Count % 2 == 1)
            {
                TermMatch termMatch; 
                
                
                // We need even values to make the last work. 
                if (typeof(TTermType) == typeof((string Term, bool Exact)) && (object)inTerms[^1] is (string stringTerm, bool isExact))
                    termMatch = TermQuery(isExact ? exactField : field, stringTerm, terms);
                else if (typeof(TTermType) == typeof(string))
                    termMatch = TermQuery(field, (string)(object)inTerms[^1], terms);
                else
                    termMatch = TermQuery(field, (Slice)(object)inTerms[^1], terms);

                if (inTerms.Count == 1)
                    return MultiTermMatch.Create(termMatch);
                

                stack[^1] = Or(stack[^1], termMatch, token);
            }

            int currentTerms = stack.Length;
            while (currentTerms > 1)
            {
                int termsToProcess = currentTerms / 2;
                int excessTerms = currentTerms % 2;

                for (int i = 0; i < termsToProcess; i++)
                    stack[i] = Or(stack[i * 2], stack[i * 2 + 1], token);

                if (excessTerms != 0)
                    stack[termsToProcess - 1] = Or(stack[termsToProcess - 1], stack[currentTerms - 1]);

                currentTerms = termsToProcess;
            }

            return MultiTermMatch.Create(stack[0]);
        }

        return MultiTermMatch.Create(new MultiTermMatch<InTermProvider<TTermType>>(this, field, _transaction.Allocator, new InTermProvider<TTermType>(this, field, inTerms), streamingEnabled: false, token: token));
    }
    
    public IQueryMatch AllInQuery(in FieldMetadata field, HashSet<string> allInTerms, bool skipEmptyItems = false, in CancellationToken token = default) =>
        AllInQuery<string>(field, allInTerms, skipEmptyItems, token);
    
    public IQueryMatch AllInQuery(in FieldMetadata field, HashSet<(string Term, bool Exact)> allInTerms, bool skipEmptyItems = false, in CancellationToken token = default) =>
        AllInQuery<(string, bool)>(field, allInTerms, skipEmptyItems, token);

    public IQueryMatch AllInQuery(in FieldMetadata field, HashSet<Slice> allInTerms, bool skipEmptyItems = false, in CancellationToken token = default) => AllInQuery<Slice>(field, allInTerms, skipEmptyItems, token);

    //Unlike the In operation, this one requires us to check all entries in a given entry.
    //However, building a query with And can quickly lead to a Stackoverflow Exception.
    //In this case, when we get more conditions, we have to quit building the tree and manually check the entries with UnaryMatch.
    private IQueryMatch AllInQuery<TTerm>(in FieldMetadata field, HashSet<TTerm> allInTerms, bool skipEmptyItems = false, in CancellationToken cancellationToken = default)
    {
        if (typeof(TTerm) != typeof((string Term, bool Exact)) && typeof(TTerm) != typeof(Slice) && typeof(TTerm) != typeof(string))
            throw new NotSupportedException($"Type {typeof(TTerm)} is not supported.");
        
        const int maximumTermMatchesHandledAsTermMatches = 4;
        
        var canUseUnaryMatch = field.HasBoost == false;
        var terms = _fieldsTree?.CompactTreeFor(field.FieldName);

        if (terms == null)
        {
            // If either the term or the field does not exist the request will be empty. 
            return MultiTermMatch.CreateEmpty(_transaction.Allocator);
        }

        //TODO PERF
        //Since comparing lists is expensive, we will try to reduce the set of likely candidates as much as possible.
        //Therefore, we check the density of elements present in the tree.
        //In the future, this can be optimized by adding some values at which it makes sense to skip And and go directly into checking.
        TermQueryItem[] queryTerms = new TermQueryItem[allInTerms.Count];
        var termsCount = 0;
        var llt = _transaction.LowLevelTransaction;

        foreach (var item in allInTerms)
        {
            Slice itemSlice;
            if (typeof(TTerm) == typeof((string Term, bool Exact)))
            {
                var casted = ((string Term, bool Exact))(object)item;
                itemSlice = casted.Exact 
                    ? EncodeAndApplyAnalyzer(default, casted.Term) 
                    : EncodeAndApplyAnalyzer(field, casted.Term);
            }
            else if (typeof(TTerm) == typeof(string))
                itemSlice = EncodeAndApplyAnalyzer(field, (string)(object)item);
            else
                itemSlice = (Slice)(object)item;
            
            if (itemSlice.Size == 0 && skipEmptyItems)
                continue;

            var amount = NumberOfDocumentsUnderSpecificTerm(terms, itemSlice);
            if (amount == 0)
            {
                return MultiTermMatch.CreateEmpty(_transaction.Allocator);
            }

            var itemKey = llt.AcquireCompactKey();
            itemKey.Set(itemSlice.AsReadOnlySpan());
            queryTerms[termsCount++] = new TermQueryItem(itemKey, amount);
        }
        
        
        //UnaryMatch doesn't support boosting, when we wants to calculate ranking we've to build query like And(Term, And(...)).
        var termMatchCount = (canUseUnaryMatch, termsCount) switch
        {
            (false, _) => termsCount,
            (true, >= maximumTermMatchesHandledAsTermMatches) => maximumTermMatchesHandledAsTermMatches,
            (true, _) => termsCount
        };
        
        
        var binaryMatchOfTermMatches = new BinaryMatch[termMatchCount / 2];
        for (int i = 0; i < termMatchCount / 2; i++)
        {
            var term1 = TermQuery(field, queryTerms[i * 2].Item, terms);
            var term2 = TermQuery(field, queryTerms[i * 2 + 1].Item, terms);
            binaryMatchOfTermMatches[i] = And(term1, term2, cancellationToken);
        }

        if (termMatchCount % 2 == 1)
        {
            // We need even values to make the last work. 
            var term = TermQuery(field, queryTerms[^1].Item, terms);

            if (allInTerms.Count == 1)
                return term;
            
            binaryMatchOfTermMatches[^1] = And(binaryMatchOfTermMatches[^1], term, cancellationToken);
        }
        
        int currentTerms = binaryMatchOfTermMatches.Length;
        while (currentTerms > 1)
        {
            int termsToProcess = currentTerms / 2;
            int excessTerms = currentTerms % 2;

            for (int i = 0; i < termsToProcess; i++)
                binaryMatchOfTermMatches[i] = And(binaryMatchOfTermMatches[i * 2], binaryMatchOfTermMatches[i * 2 + 1]);

            if (excessTerms != 0)
                binaryMatchOfTermMatches[termsToProcess - 1] = And(binaryMatchOfTermMatches[termsToProcess - 1], binaryMatchOfTermMatches[currentTerms - 1]);

            currentTerms = termsToProcess;
        }

        //Just perform normal And.
        if (termsCount == termMatchCount)
            return MultiTermMatch.Create(binaryMatchOfTermMatches[0]);


        queryTerms = queryTerms[termMatchCount..];
        queryTerms.AsSpan().Sort(CompareTermQueryItemKeys);
        return UnaryQuery(binaryMatchOfTermMatches[0], field, queryTerms, UnaryMatchOperation.AllIn, -1, cancellationToken);
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public AndNotMatch NotInQuery<TInner>(FieldMetadata field, TInner inner, List<string> notInTerms) where TInner : IQueryMatch
    {
        return AndNot(inner, MultiTermMatch.Create(new MultiTermMatch<InTermProvider<string>>(this, field, _transaction.Allocator, new InTermProvider<string>(this, field, notInTerms), streamingEnabled: false)));
    }
}
