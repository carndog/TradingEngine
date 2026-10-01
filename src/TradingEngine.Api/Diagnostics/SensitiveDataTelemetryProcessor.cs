using System.Diagnostics;
using OpenTelemetry;

namespace TradingEngine.Api.Diagnostics;

internal sealed class SensitiveDataTelemetryProcessor : BaseProcessor<Activity>
{
    private const string UrlFullTag = "url.full";
    private const string HttpUrlTag = "http.url";
    private const string UrlQueryTag = "url.query";
    private const string HeaderTagPrefix = "http.request.header.";

    private static readonly string[] StatementTags =
    [
        "db.statement",
        "db.query.text"
    ];

    public override void OnEnd(Activity activity)
    {
        foreach (string statementTag in StatementTags)
        {
            activity.SetTag(statementTag, null);
        }

        activity.SetTag(UrlQueryTag, null);
        RemoveHeaderTags(activity);
        RemoveQueryString(activity, UrlFullTag);
        RemoveQueryString(activity, HttpUrlTag);
    }

    private static void RemoveHeaderTags(Activity activity)
    {
        List<string> headerKeys = [];
        foreach (KeyValuePair<string, object?> tag in activity.TagObjects)
        {
            if (tag.Key.StartsWith(HeaderTagPrefix, StringComparison.Ordinal))
            {
                headerKeys.Add(tag.Key);
            }
        }

        foreach (string key in headerKeys)
        {
            activity.SetTag(key, null);
        }
    }

    private static void RemoveQueryString(Activity activity, string tagName)
    {
        if (activity.GetTagItem(tagName) is not string url)
        {
            return;
        }

        int queryStart = url.IndexOfAny('?', '#');
        if (queryStart == -1)
        {
            return;
        }

        activity.SetTag(tagName, url.Substring(0, queryStart));
    }
}
