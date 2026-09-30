using System.Collections.Generic;

namespace YARG.Core.Song.Recommendations
{
    /// <summary>
    /// Keeps a set of picks varied: each song (across all its charts) and each artist appears once, and
    /// genres are capped per group (such as a recommendation row) and overall.
    /// </summary>
    internal sealed class VarietyRules
    {
        private readonly int _genrePerGroup;
        private readonly int _genreTotal;

        private readonly HashSet<string> _identities = new();
        private readonly HashSet<string> _artists = new();
        private readonly Dictionary<(int, string), int> _genreInGroup = new();
        private readonly Dictionary<string, int> _genreOverall = new();

        public VarietyRules(int genrePerGroup = int.MaxValue, int genreTotal = int.MaxValue)
        {
            _genrePerGroup = genrePerGroup;
            _genreTotal = genreTotal;
        }

        public bool Allows(SongFacts song, int group = 0)
        {
            return !_identities.Contains(song.Identity) &&
                (song.Artist == null || !_artists.Contains(song.Artist)) &&
                (song.Genre == null || (Get(_genreInGroup, (group, song.Genre)) < _genrePerGroup &&
                    Get(_genreOverall, song.Genre) < _genreTotal));
        }

        /// <summary>
        /// Adds the song if the rules allow it; returns whether it was added.
        /// </summary>
        public bool TryAdd(SongFacts song, int group = 0)
        {
            if (!Allows(song, group)) return false;

            _identities.Add(song.Identity);
            if (song.Artist != null) _artists.Add(song.Artist);
            if (song.Genre != null)
            {
                _genreInGroup[(group, song.Genre)] = Get(_genreInGroup, (group, song.Genre)) + 1;
                _genreOverall[song.Genre] = Get(_genreOverall, song.Genre) + 1;
            }

            return true;
        }

        private static int Get<TKey>(Dictionary<TKey, int> counts, TKey key) where TKey : notnull =>
            counts.TryGetValue(key, out int n) ? n : 0;
    }
}
