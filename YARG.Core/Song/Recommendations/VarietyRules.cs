using System.Collections.Generic;

namespace YARG.Core.Song.Recommendations
{
    /// <summary>
    /// Keeps a set of picks varied: each song appears once (across all its charts), and artists and genres
    /// are capped per group (such as a recommendation row) and overall.
    /// </summary>
    public sealed class VarietyRules
    {
        private readonly int _artistPerGroup;
        private readonly int _artistTotal;
        private readonly int _genrePerGroup;
        private readonly int _genreTotal;

        private readonly HashSet<string> _identities = new();
        private readonly Dictionary<(int, string), int> _groupCounts = new();
        private readonly Dictionary<string, int> _totalCounts = new();

        public VarietyRules(int artistPerGroup, int artistTotal, int genrePerGroup = int.MaxValue,
            int genreTotal = int.MaxValue)
        {
            _artistPerGroup = artistPerGroup;
            _artistTotal = artistTotal;
            _genrePerGroup = genrePerGroup;
            _genreTotal = genreTotal;
        }

        public bool Allows(SongFacts song, int group = 0)
        {
            return !_identities.Contains(song.Identity) &&
                UnderCap(group, "a:", song.Artist, _artistPerGroup, _artistTotal) &&
                UnderCap(group, "g:", song.Genre, _genrePerGroup, _genreTotal);
        }

        /// <summary>
        /// Adds the song if the rules allow it; returns whether it was added.
        /// </summary>
        public bool TryAdd(SongFacts song, int group = 0)
        {
            if (!Allows(song, group)) return false;

            _identities.Add(song.Identity);
            Count(group, "a:", song.Artist);
            Count(group, "g:", song.Genre);
            return true;
        }

        private bool UnderCap(int group, string kind, string? value, int perGroup, int total)
        {
            if (value == null) return true;

            string key = kind + value;
            return Get(_groupCounts, (group, key)) < perGroup && Get(_totalCounts, key) < total;
        }

        private void Count(int group, string kind, string? value)
        {
            if (value == null) return;

            string key = kind + value;
            _groupCounts[(group, key)] = Get(_groupCounts, (group, key)) + 1;
            _totalCounts[key] = Get(_totalCounts, key) + 1;
        }

        private static int Get<TKey>(Dictionary<TKey, int> counts, TKey key) where TKey : notnull =>
            counts.TryGetValue(key, out int n) ? n : 0;
    }
}
