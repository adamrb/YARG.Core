using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace YARG.Core.Song.Recommendations
{
    /// <summary>
    /// Where songs sit on a co-listening map built from ListenBrainz data (CC0): songs the same people listen
    /// to sit close together, and each artist sits among its songs. Also knows how popular each song is,
    /// overall and among its artist's songs. Built by Tools/SongMap/build_song_map.py in the YARG repo.
    /// </summary>
    /// <remarks>
    /// Lines are "a", artist, values, or "t", artist|title (see <see cref="SongNormalizer.Identity"/>),
    /// listener count, artist rank (0 for the artist's most played song, 1 for the least), values. Values
    /// are separated by tabs; lines starting with '#' are comments.
    /// </remarks>
    public sealed class SongMap
    {
        public static readonly SongMap Empty = new();

        private readonly struct Track
        {
            public readonly float[] Position;
            public readonly int Listeners;
            public readonly float ArtistRank;

            public Track(float[] position, int listeners, float artistRank)
            {
                Position = position;
                Listeners = listeners;
                ArtistRank = artistRank;
            }
        }

        // Listener counts as features, least listened first: fewer than Below listeners gets Name
        private static readonly (int Below, string Name)[] ListenerBuckets =
        {
            (200, "under 200"), (1000, "under 1000"), (5000, "under 5000"), (int.MaxValue, "5000 or more"),
        };

        private readonly Dictionary<string, float[]> _artists = new();
        private readonly Dictionary<string, Track> _tracks = new();
        private int _dims;

        public int ArtistCount => _artists.Count;
        public int TrackCount => _tracks.Count;

        /// <summary>
        /// Reads the map, skipping lines that are malformed, have non-finite values, or have a different
        /// number of values than the first line. Positions are scaled to unit length.
        /// </summary>
        public static SongMap Parse(IEnumerable<string> lines)
        {
            var map = new SongMap();
            foreach (string line in lines)
            {
                if (line.Length < 2 || line[0] == '#') continue;

                var parts = line.Split('\t');
                if (parts[0] == "a" && parts.Length >= 3 && map.ReadPosition(parts, 2) is { } artist)
                {
                    map._artists[parts[1]] = artist;
                    map._dims = artist.Length;
                }
                else if (parts[0] == "t" && parts.Length >= 5 &&
                    int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int listeners) && listeners >= 0 &&
                    float.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out float rank) &&
                    rank is >= 0f and <= 1f && map.ReadPosition(parts, 4) is { } track)
                {
                    map._tracks[parts[1]] = new Track(track, listeners, rank);
                    map._dims = track.Length;
                }
            }

            return map;
        }

        /// <summary>
        /// Fills in what the map knows about a song: its position (its own, or its artist's if the map does
        /// not know the song), its artist's position, whether the map knows the song itself, and its
        /// popularity, added to its features.
        /// </summary>
        /// <param name="artist">The artist as written in song metadata; normalized here.</param>
        /// <param name="title">The title as written in song metadata; normalized here.</param>
        public void Place(SongFacts song, string? artist, string? title)
        {
            string artistKey = SongNormalizer.Artist(artist);
            song.ArtistPosition = _artists.TryGetValue(artistKey, out var artistPosition) ? artistPosition : null;
            song.OnMap = _tracks.TryGetValue(artistKey + "|" + SongNormalizer.Title(title), out var track);
            song.Position = song.OnMap ? track.Position : song.ArtistPosition;
            if (song.OnMap)
            {
                song.Features = song.Features.Append(new SongFeature(FeatureType.ArtistRank, track.ArtistRank switch
                {
                    <= 0.1f => "hit",
                    <= 0.4f => "known",
                    _       => "deep cut",
                })).Append(new SongFeature(FeatureType.Listeners,
                    ListenerBuckets.First(b => track.Listeners < b.Below).Name)).ToArray();
            }
        }

        /// <summary>
        /// How many people listen to the song, as a rank from 0 (unknown to the map) to 4 (the most listened).
        /// </summary>
        public static int PopularityRank(SongFacts song)
        {
            foreach (var feature in song.Features)
            {
                if (feature.Type == FeatureType.Listeners)
                {
                    return Array.FindIndex(ListenerBuckets, b => b.Name == feature.Value) + 1;
                }
            }

            return 0;
        }

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

        private float[]? ReadPosition(string[] parts, int start)
        {
            int dims = parts.Length - start;
            if (_dims > 0 && dims != _dims) return null;

            var values = new float[dims];
            double length = 0;
            for (int i = 0; i < dims; i++)
            {
                if (!float.TryParse(parts[start + i], NumberStyles.Float, CultureInfo.InvariantCulture, out values[i]) ||
                    float.IsNaN(values[i]) || float.IsInfinity(values[i]))
                {
                    return null;
                }

                length += (double) values[i] * values[i];
            }

            double norm = Math.Sqrt(length);
            if (norm == 0) return null;

            for (int i = 0; i < dims; i++) values[i] = (float) (values[i] / norm);
            return values;
        }
    }
}
