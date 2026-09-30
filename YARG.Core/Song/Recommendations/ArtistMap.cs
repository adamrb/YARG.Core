using System;
using System.Collections.Generic;
using System.Globalization;

namespace YARG.Core.Song.Recommendations
{
    /// <summary>
    /// Artist positions on a co-listening map built from ListenBrainz data (CC0): artists the same people
    /// listen to sit close together. Loaded from lines of "normalized artist name", a tab, and the values;
    /// lines starting with '#' are comments. Built by Tools/ArtistMap/build_artist_map.py in the YARG repo.
    /// </summary>
    public sealed class ArtistMap
    {
        public static readonly ArtistMap Empty = new();

        private readonly Dictionary<string, float[]> _positions = new();

        public int Count => _positions.Count;

        /// <summary>
        /// Reads the map, skipping lines that are malformed, have non-finite values, or have a different
        /// number of values than the first line. Positions are scaled to unit length.
        /// </summary>
        public static ArtistMap Parse(IEnumerable<string> lines)
        {
            var map = new ArtistMap();
            int dims = 0;
            foreach (string line in lines)
            {
                if (line.Length == 0 || line[0] == '#') continue;

                var parts = line.Split('\t');
                if (parts.Length < 2 || (dims > 0 && parts.Length - 1 != dims)) continue;

                var values = new float[parts.Length - 1];
                double length = 0;
                bool valid = true;
                for (int i = 1; i < parts.Length && valid; i++)
                {
                    valid = float.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out values[i - 1])
                        && !float.IsNaN(values[i - 1]) && !float.IsInfinity(values[i - 1]);
                    length += (double) values[i - 1] * values[i - 1];
                }

                if (!valid || length == 0) continue;

                float scale = (float) (1 / Math.Sqrt(length));
                for (int i = 0; i < values.Length; i++) values[i] *= scale;
                map._positions[parts[0]] = values;
                dims = values.Length;
            }

            return map;
        }

        /// <param name="artist">An artist as written in song metadata; normalized here.</param>
        public float[]? Find(string? artist) =>
            _positions.TryGetValue(SongNormalizer.Artist(artist), out var position) ? position : null;

        /// <summary>
        /// Cosine similarity of two positions (they are unit length), or 0 if either is missing.
        /// </summary>
        public static float Similarity(float[]? a, float[]? b)
        {
            if (a == null || b == null || a.Length != b.Length) return 0f;

            float dot = 0f;
            for (int i = 0; i < a.Length; i++) dot += a[i] * b[i];
            return dot;
        }
    }
}
