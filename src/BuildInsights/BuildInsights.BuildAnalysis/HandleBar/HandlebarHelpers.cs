// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections;
using HandlebarsDotNet;
using HandlebarsDotNet.Helpers;
using Microsoft.Extensions.DependencyInjection;

namespace BuildInsights.BuildAnalysis.HandleBar;

public class HandlebarHelpers
{
    public const int ResultsLimit = 3;

    private readonly IServiceProvider _provider;

    public HandlebarHelpers(IServiceProvider provider)
    {
        _provider = provider;
    }

    public void AddHelpers(IHandlebars hb)
    {
        foreach (var helper in _provider.GetServices<IHelperDescriptor<HelperOptions>>())
        {
            hb.RegisterHelper(helper);
        }

        TargetBranchName(hb);
        DateTimeFormatter(hb);
        LimitedEach(hb);
        OrHelper(hb);
        EqHelper(hb);
        GreaterThanLimit(hb);
        GetNumberOfRecordsNotDisplayed(hb);
        ForTake(hb);
    }

    private static void TargetBranchName(IHandlebars hb)
    {
        hb.RegisterHelper("TargetBranchName", (writer, context, parameters) =>
        {
            if (parameters.Length == 1 && parameters[0] is string name)
            {
                writer.Write(name);
            }
            else
            {
                writer.WriteSafeString("target branch");
            }
        });
    }

    private static void GreaterThanLimit(IHandlebars hb)
    {
        hb.RegisterHelper("gt", (context, parameters) =>
        {
            if (parameters.Length != 1 || parameters[0] is not int)
            {
                throw new HandlebarsException("{{#AreRecordsNotDisplayed}} helper must have an int");
            }
            int totalRecords = (int)parameters[0];
            int numberOfResultsNotDisplayed = totalRecords - ResultsLimit;
            return numberOfResultsNotDisplayed > 0;
        });
    }

    private static void GetNumberOfRecordsNotDisplayed(IHandlebars hb)
    {
        hb.RegisterHelper("GetNumberOfRecordsNotDisplayed", (writer, context, parameters) =>
        {
            if (parameters.Length != 1 || parameters[0] is not int)
            {
                throw new HandlebarsException("{{#GetNumberOfRecordsNotDisplayed}} helper must have an int");
            }

            int totalRecords = (int)parameters[0];
            int numberOfResultsNotDisplayed = totalRecords - ResultsLimit;
            writer.Write($"{numberOfResultsNotDisplayed}");
        });
    }

    private static void OrHelper(IHandlebars hb)
    {
        hb.RegisterHelper("or", (context, parameters) =>
        {
            if (parameters.Length < 2)
                throw new HandlebarsException("{{# (or ) }} helper must have at least two arguments");

            bool orResult = false;
            foreach (object obj in parameters)
            {
                if (obj is not bool objBool)
                    throw new HandlebarsException("{{# (or ) }} helper only supports arguments of type: bool");

                orResult = objBool || orResult;
            }

            return orResult;
        });
    }

    /// <summary>
    /// Registers helper for string comparison in handlebar <paramref name="hb"/>
    /// The string comparison is case sensitive
    /// </summary>

    private static void EqHelper(IHandlebars hb)
    {
        hb.RegisterHelper("eq", (context, parameters) =>
        {
            if (parameters.Length != 2)
                throw new HandlebarsException("{{# (eq ) }} helper must have two arguments");

            return parameters[0].ToString().Equals(parameters[1].ToString());
        });
    }

    private static void LimitedEach(IHandlebars hb)
    {
        hb.RegisterHelper("limited-each", (writer, options, context, arguments) =>
        {
            if (arguments.Length == 0)
            {
                throw new HandlebarsException("{{#limited-each}} helper must have an ICollection argument");
            }

            if (arguments[0] is ICollection collection)
            {
                IEnumerator enumerator = collection.GetEnumerator();
                for (int i = 0; i < ResultsLimit; i++)
                {
                    if (!enumerator.MoveNext())
                    {
                        break;
                    }

                    options.Template(writer, enumerator.Current);
                }

                int countNotShowedResults = collection.Count - ResultsLimit;
                if (arguments.Length > 2 && countNotShowedResults > 0)
                {
                    string markdownFormat = arguments[1] as string;
                    string message = arguments[2] as string;

                    writer.WriteSafeString($"{markdownFormat} {countNotShowedResults} {message} \n");
                }
            }
        });
    }

    private static void ForTake(IHandlebars hb)
    {
        hb.RegisterHelper("for-take", (writer, options, context, arguments) =>
        {
            if (arguments.Length < 2)
            {
                throw new HandlebarsException("{{#for-take}} helper must have an ICollection argument and an int argument");
            }

            if (arguments[0] is not ICollection collection)
            {
                throw new HandlebarsException("{{#for-take}} helper expects argument to be ICollection");
            }

            if (arguments[1] is not int takeRecords || takeRecords < 0)
            {
                throw new HandlebarsException("{{#for-take}} helper expects argument to be positive int");
            }

            int skippedResultsCount = collection.Count - takeRecords;
            if (arguments.Length == 4 && skippedResultsCount > 0)
            {
                if (arguments[2] is not string markdownFormat)
                {
                    throw new HandlebarsException("{{for-take}} helper expects third argument to be string");
                }

                if (arguments[3] is not string message)
                {
                    throw new HandlebarsException("{{for-take}} helper expects fourth argument to be string");
                }

                writer.WriteSafeString($"{markdownFormat} {skippedResultsCount} {message} \n");
            }

            IEnumerator enumerator = collection.GetEnumerator();
            for (int i = 0; i < takeRecords; i++)
            {
                if (!enumerator.MoveNext())
                {
                    break;
                }

                options.Template(writer, enumerator.Current);
            }
        });
    }

    private static void DateTimeFormatter(IHandlebars hb)
    {
        var formatter = new CustomDateTimeFormatter("yyyy-MM-dd");
        hb.Configuration.FormatterProviders.Add(formatter);
    }
}
