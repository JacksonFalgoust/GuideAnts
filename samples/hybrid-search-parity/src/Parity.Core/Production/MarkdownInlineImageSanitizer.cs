using System.Text.RegularExpressions;

namespace Parity.Core.Production;

// Copied verbatim from src/server/GuideAntsApi.BackgroundJobs/Services/Indexing/MarkdownInlineImageSanitizer.cs @ 9f8cd102.
/// <summary>
/// Replaces inline <c>data:image/...;base64,...</c> payloads in markdown/HTML-like text with a short placeholder.
/// </summary>
public static class MarkdownInlineImageSanitizer
{
    public const string InlineImagePlaceholder = "[inline-image omitted]";

    public static readonly Regex InlineImageDataUrlRegex = new(
        @"data:image\/[a-z0-9.+-]+;base64,[A-Za-z0-9+\/=%\\\s_-]+",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static (string SanitizedText, int ReplacementCount) ReplaceInlineImageDataUrls(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return (text ?? string.Empty, 0);
        }

        var replacements = 0;
        var sanitized = InlineImageDataUrlRegex.Replace(text, _ =>
        {
            replacements++;
            return InlineImagePlaceholder;
        });
        return (sanitized, replacements);
    }
}
