using System.Text.Json;

namespace HomeServer.Metadata;

public static class AgeRatings
{
    public static (int? Age, string? Label) Read(JsonElement root)
    {
        if (root.TryGetProperty("adult", out var adult) && adult.ValueKind == JsonValueKind.True) return (18, "TMDB: adult");
        var ratings = new List<(string Region, string Label)>();
        if (root.TryGetProperty("release_dates", out var dates) && dates.TryGetProperty("results", out var releases))
            foreach (var region in releases.EnumerateArray())
                if (region.TryGetProperty("release_dates", out var entries))
                    foreach (var entry in entries.EnumerateArray())
                        if (entry.TryGetProperty("certification", out var certification) && certification.GetString() is { Length: > 0 } value)
                            ratings.Add((region.GetProperty("iso_3166_1").GetString() ?? "", value));
        if (root.TryGetProperty("content_ratings", out var contentRatings) && contentRatings.TryGetProperty("results", out var results))
            foreach (var entry in results.EnumerateArray())
                if (entry.TryGetProperty("rating", out var rating) && rating.GetString() is { Length: > 0 } value)
                    ratings.Add((entry.GetProperty("iso_3166_1").GetString() ?? "", value));
        foreach (var region in new[] { "TR", "US", "GB" })
        {
            var candidates = ratings.Where(value => value.Region == region).Select(value => (Age: Normalize(region, value.Label), value.Label)).Where(value => value.Age is not null).OrderByDescending(value => value.Age).ToArray();
            if (candidates.Length > 0) return (candidates[0].Age, region + ": " + candidates[0].Label);
        }
        return (null, ratings.FirstOrDefault() is { Label: not null } first ? first.Region + ": " + first.Label : null);
    }
    public static int? Normalize(string region, string label)
    {
        var value = label.Trim().ToUpperInvariant();
        if (region == "US") return value switch { "G" or "TV-G" or "TV-Y" => 0, "TV-Y7" or "TV-Y7-FV" => 7, "PG-13" => 13, "R" or "TV-MA" => 17, "NC-17" => 18, _ => null };
        if (region == "GB") return value switch { "U" => 0, "12" or "12A" => 12, "15" => 15, "18" or "R18" => 18, _ => null };
        if (region == "TR") return value switch { "GENEL İZLEYİCİ" or "GENEL IZLEYICI" or "G" => 0, "6+" or "6A" => 6, "7+" => 7, "10+" or "10A" => 10, "13+" or "13A" => 13, "16+" or "16A" => 16, "18+" => 18, _ => null };
        return null;
    }
}
