﻿using System;
using System.Text;
using Caravela.Framework.Aspects;
using Caravela.Framework.Code;
using static Caravela.Framework.Aspects.TemplateContext;

public class LogAttribute : OverrideMethodAspect
{
    public override dynamic OverrideMethod()
    {
        Console.WriteLine(target.Method.ToDisplayString() + " started.");

        try
        {
            dynamic result = proceed();

            Console.WriteLine(target.Method.ToDisplayString() + " succeeded.");
            return result;
        }
        catch (Exception e)
        {
            Console.WriteLine(target.Method.ToDisplayString() + " failed: " + e.Message);

            throw;
        }
    }
}


public class CacheAttribute : OverrideMethodAspect
{
    public override dynamic OverrideMethod()
    {
        // Builds the caching string.
        var stringBuilder = compileTime(new StringBuilder());
        stringBuilder.Append(target.Type.ToString());
        stringBuilder.Append('.');
        stringBuilder.Append(target.Method.Name);
        stringBuilder.Append('(');
        int i = compileTime(0);
        foreach (var p in target.Parameters)
        {
            string comma = i > 0 ? ", " : "";

            if (p.IsOut())
            {
                stringBuilder.Append($"{comma}{p.Name} = <out> ");
            }
            else
            {
                stringBuilder.Append($"{comma}{{{i}}}");
            }

            i++;
        }
        stringBuilder.Append(')');

        string cacheKey = string.Format(stringBuilder.ToString(), target.Parameters.Values.ToArray());

        // Cache lookup.
        if (SampleCache.Cache.TryGetValue(cacheKey, out object value))
        {
            Console.WriteLine("Cache hit.");
            return value;
        }
        else
        {
            Console.WriteLine("Cache miss.");

            // Bug 28455 (lexical conflict with [Log]). 
            dynamic result2 = proceed();

            // Add to cache.
            SampleCache.Cache.TryAdd(cacheKey, result2);
            return result2;
        }
    }
}

// Placeholder implementation of a cache because the hosted try.postsharp.net does not allow for MemoryCache.
public static class SampleCache
{
    public static readonly System.Collections.Concurrent.ConcurrentDictionary<string, object> Cache =
        new System.Collections.Concurrent.ConcurrentDictionary<string, object>();
}