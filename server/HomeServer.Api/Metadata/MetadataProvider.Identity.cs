using System.Text.Json;
using HomeServer.Core;

namespace HomeServer.Metadata;

public sealed partial class MetadataProvider
{
    public async Task<ProviderEpisode[]> DailyEpisodesAsync(string provider, int showId, string date, CancellationToken token)
    {
        Validate(provider, "show");
        if (!DateOnly.TryParseExact(date, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out _)) throw new DomainException("Yayın tarihi geçersiz.");
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(token); budget.CancelAfter(TimeSpan.FromMinutes(2));
        try
        {
        ProviderEpisode[] episodes;
        if (provider == "tvmaze")
        {
            using var json = await GetAsync(provider, $"shows/{showId}/episodesbydate?date={date}", budget.Token);
            episodes = json.RootElement.EnumerateArray().Where(value => value.TryGetProperty("number", out var number) && number.ValueKind == JsonValueKind.Number && number.GetInt32() > 0)
                .Select(value => new ProviderEpisode(value.GetProperty("id").GetInt32(), value.GetProperty("season").GetInt32(), value.GetProperty("number").GetInt32(), Text(value, "name") ?? "", PlainText(Text(value, "summary") ?? ""), Text(value, "airdate"), TvmazeImage(value, "original"), EpisodeRuntime(value))).ToArray();
        }
        else episodes = (await EpisodesAsync(provider, showId, budget.Token)).Where(episode => episode.AirDate == date).ToArray();
        if (episodes.Length != 1 || episodes[0].AirDate != date) throw new DomainException(episodes.Length > 1 ? "Aynı tarihte birden fazla bölüm var; sezon/bölüm karşılığını seç." : "Bu tarihte tekil bölüm doğrulanamadı.", 409);
        return episodes;
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { throw new DomainException("Günlük bölüm katalog sorgusu süre sınırına ulaştı.", 504); }
    }
    public async Task<string[]> AlternativeNamesAsync(ProviderContent content, CancellationToken token)
    {
        Validate(content.Provider, content.Kind);
        var route = content.Provider == "tvmaze" ? $"shows/{content.ProviderId}/akas" : $"{(content.Kind == "movie" ? "movie" : "tv")}/{content.ProviderId}/alternative_titles";
        using var json = await GetAsync(content.Provider, route, token);
        if (content.Provider == "tmdb" && (json.RootElement.ValueKind != JsonValueKind.Object || !json.RootElement.TryGetProperty("id", out var id) || id.ValueKind != JsonValueKind.Number || !id.TryGetInt32(out var parentId) || parentId != content.ProviderId)) throw new DomainException("Alternatif adlar farklı bir yapımı bildiriyor.", 502);
        var items = content.Provider == "tvmaze" ? json.RootElement : json.RootElement.GetProperty(content.Kind == "movie" ? "titles" : "results");
        return items.EnumerateArray().Select(value => Text(value, content.Provider == "tvmaze" ? "name" : "title")).Where(value => !string.IsNullOrWhiteSpace(value) && value.Length <= 300).Cast<string>().Distinct().Take(200).ToArray();
    }
    public async Task<ProviderEpisodeOrder[]> EpisodeOrdersAsync(int showId, CancellationToken token)
    {
        if (showId <= 0) throw new DomainException("Dizi kimliği geçersiz.");
        using var json = await GetAsync("tmdb", $"tv/{showId}/episode_groups", token);
        if (json.RootElement.GetProperty("id").GetInt32() != showId) throw new DomainException("Bölüm grubu farklı bir diziye ait.", 502);
        return json.RootElement.GetProperty("results").EnumerateArray().Take(100).Select(value => new ProviderEpisodeOrder(Text(value, "id") ?? "", Text(value, "name") ?? "", Text(value, "description") ?? "", value.GetProperty("type").GetInt32(), value.GetProperty("episode_count").GetInt32())).ToArray();
    }
    public async Task<ImportedEpisodeOrder> AbsoluteOrderAsync(int showId, string orderId, CancellationToken token)
    {
        if (orderId.Length != 24 || orderId.Any(character => !char.IsAsciiHexDigit(character))) throw new DomainException("Bölüm grubu kimliği geçersiz.");
        var order = (await EpisodeOrdersAsync(showId, token)).FirstOrDefault(value => value.Id == orderId && value.Type == 2) ?? throw new DomainException("Seçilen dizi için TMDB mutlak numara grubu bulunamadı.", 409);
        using var json = await GetAsync("tmdb", "tv/episode_group/" + orderId, token); var root = json.RootElement;
        if (Text(root, "id") != orderId || root.GetProperty("type").GetInt32() != 2) throw new DomainException("Bölüm grubu yanıtı eşleşmedi.", 502);
        var groups = root.GetProperty("groups").EnumerateArray().OrderBy(group => group.GetProperty("order").GetInt32()).ToArray();
        if (groups.GroupBy(group => group.GetProperty("order").GetInt32()).Any(group => group.Count() != 1)) throw new DomainException("Bölüm grubu sıralaması çelişkili.", 502);
        var entries = new List<NumberingEntry>();
        foreach (var group in groups)
        {
            var episodes = group.GetProperty("episodes").EnumerateArray().OrderBy(ep => ep.GetProperty("order").GetInt32()).ToArray();
            for (var index = 0; index < episodes.Length; index++)
            {
                var ep = episodes[index];
                if (ep.GetProperty("order").GetInt32() != index || ep.GetProperty("show_id").GetInt32() != showId || entries.Count >= 20000) throw new DomainException("Mutlak bölüm sırası eksik veya yanlış dizi içeriyor.", 502);
                entries.Add(new NumberingEntry(entries.Count + 1, ep.GetProperty("season_number").GetInt32(), ep.GetProperty("episode_number").GetInt32(), ep.GetProperty("id").GetInt32()));
            }
        }
        if (entries.Count == 0 || entries.Count != root.GetProperty("episode_count").GetInt32() || entries.Count != order.EpisodeCount || entries.Select(entry => entry.ProviderId).Distinct().Count() != entries.Count) throw new DomainException("Mutlak bölüm grubu eksik veya tekrarlı.", 502);
        return new(orderId, order.Name, entries.ToArray());
    }
    public async Task<ProviderOrderDetails> EpisodeOrderDetailsAsync(int showId, string orderId, CancellationToken token)
    {
        if (orderId.Length != 24 || orderId.Any(character => !char.IsAsciiHexDigit(character))) throw new DomainException("Bölüm grubu kimliği geçersiz.");
        var summary = (await EpisodeOrdersAsync(showId, token)).FirstOrDefault(order => order.Id == orderId) ?? throw new DomainException("Bu diziye ait bölüm sırası bulunamadı.", 404);
        using var json = await GetAsync("tmdb", "tv/episode_group/" + orderId, token); var root = json.RootElement;
        if (Text(root, "id") != orderId || root.GetProperty("type").GetInt32() != summary.Type) throw new DomainException("Bölüm grubu kimliği veya türü değişti.", 502);
        var groups = new List<ProviderOrderGroup>(); var count = 0;
        foreach (var group in root.GetProperty("groups").EnumerateArray().OrderBy(group => group.GetProperty("order").GetInt32()))
        {
            var id = Text(group, "id"); if (string.IsNullOrEmpty(id) || id.Length > 100 || groups.Count >= 1001) throw new DomainException("Bölüm grubu sınırları geçersiz.", 502);
            var rows = group.GetProperty("episodes").EnumerateArray().OrderBy(episode => episode.GetProperty("order").GetInt32()).ToArray(); var episodes = new List<ProviderEpisode>();
            for (var index = 0; index < rows.Length; index++)
            {
                var ep = rows[index];
                if (++count > 20000 || ep.GetProperty("order").GetInt32() != index || ep.GetProperty("show_id").GetInt32() != showId) throw new DomainException("Sağlayıcı sırası eksik, tekrarlı veya başka dizi içeriyor.", 502);
                episodes.Add(new(ep.GetProperty("id").GetInt32(), ep.GetProperty("season_number").GetInt32(), ep.GetProperty("episode_number").GetInt32(), Text(ep, "name") ?? "", PlainText(Text(ep, "overview") ?? ""), Text(ep, "air_date"), TmdbImage(Text(ep, "still_path"), "w780"), EpisodeRuntime(ep)));
            }
            if (episodes.Count == 0) throw new DomainException("Bölüm grubu boş bir sıra içeriyor.", 502);
            groups.Add(new(id, Text(group, "name") ?? "", group.GetProperty("order").GetInt32(), episodes.ToArray()));
        }
        if (count == 0 || count != summary.EpisodeCount || count != root.GetProperty("episode_count").GetInt32() || root.GetProperty("group_count").GetInt32() != groups.Count
            || groups.Select(group => group.Id).Distinct().Count() != groups.Count || groups.Select(group => group.Position).Distinct().Count() != groups.Count || groups.SelectMany(group => group.Episodes).Select(episode => episode.ProviderId).Distinct().Count() != count)
            throw new DomainException("Sağlayıcı bölüm sırası tam ve tekil değil.", 502);
        var result = new ProviderOrderDetails(orderId, summary.Name, summary.Type, groups.ToArray());
        return result with { Fingerprint = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(result))) };
    }
}
