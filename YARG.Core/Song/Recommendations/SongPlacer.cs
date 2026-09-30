using System;
using System.Collections.Generic;
using System.Linq;

namespace YARG.Core.Song.Recommendations
{
    /// <summary>
    /// Places songs by artists the <see cref="SongMap"/> does not know, such as bands written for rhythm
    /// games. A ridge regression learns, from the library's songs that the map does know, how where a song
    /// sits relates to its genre, charter, source, decade and length, and predicts a position for the rest.
    /// It is fitted to each library, so it learns that library's charters, sources and genre spellings.
    /// Songs whose artist is on the map keep their artist's position, which places them better.
    /// </summary>
    public static class SongPlacer
    {
        // With fewer known songs than this the regression would mostly fit noise
        private const int MIN_KNOWN_SONGS = 50;

        // A feature has to appear on this many known songs to be learned from
        private const int MIN_FEATURE_SONGS = 2;

        private const double L2 = 10;

        /// <summary>
        /// Gives every song with no position a predicted one. Returns how many songs were placed; none when
        /// the library has too few songs the map knows.
        /// </summary>
        public static int PlaceUnknownSongs(IEnumerable<SongFacts> library)
        {
            var songs = library.ToList();
            var known = songs.Where(s => s.OnMap && s.Position != null).ToList();
            var unplaced = songs.Where(s => s.Position == null).ToList();
            if (known.Count < MIN_KNOWN_SONGS || unplaced.Count == 0)
            {
                return 0;
            }

            int dims = known[0].Position!.Length;
            var index = known.SelectMany(s => s.Features.Where(Describes).Distinct())
                .GroupBy(feature => feature)
                .Where(group => group.Count() >= MIN_FEATURE_SONGS)
                .Select((group, i) => (group.Key, i))
                .ToDictionary(x => x.Key, x => x.i);
            int bias = index.Count;
            int size = bias + 1;

            // The columns of the design matrix a song's row touches, each with value 1
            int[] Encode(SongFacts song) => song.Features.Distinct()
                .Select(feature => index.TryGetValue(feature, out int i) ? i : -1)
                .Where(i => i >= 0)
                .Append(bias)
                .ToArray();

            // Normal equations (XᵀX + λI) W = XᵀY, small enough (one row and column per feature) to solve exactly
            var normal = new double[size, size];
            var targets = new double[size, dims];
            foreach (var song in known)
            {
                var columns = Encode(song);
                foreach (int i in columns)
                {
                    foreach (int j in columns) normal[i, j] += 1;
                    for (int d = 0; d < dims; d++) targets[i, d] += song.Position![d];
                }
            }

            for (int i = 0; i < size; i++)
            {
                normal[i, i] += L2;
            }

            var weights = SolveCholesky(normal, targets);

            int placed = 0;
            foreach (var song in unplaced)
            {
                var position = new double[dims];
                foreach (int i in Encode(song))
                {
                    for (int d = 0; d < dims; d++) position[d] += weights[i, d];
                }

                double length = Math.Sqrt(position.Sum(x => x * x));
                if (length > 0)
                {
                    song.Position = position.Select(x => (float) (x / length)).ToArray();
                    placed++;
                }
            }

            return placed;
        }

        // What describes the song itself. Popularity is known only for songs on the map, and an artist the
        // map does not know has no songs to learn from.
        private static bool Describes(SongFeature feature) =>
            feature.Type is not (FeatureType.Artist or FeatureType.ArtistRank or FeatureType.Listeners);

        /// <summary>
        /// Solves A X = B for a symmetric positive definite A (overwritten) by Cholesky decomposition.
        /// </summary>
        private static double[,] SolveCholesky(double[,] a, double[,] b)
        {
            int n = a.GetLength(0);
            int m = b.GetLength(1);
            for (int j = 0; j < n; j++)
            {
                double diagonal = a[j, j];
                for (int k = 0; k < j; k++) diagonal -= a[j, k] * a[j, k];
                a[j, j] = Math.Sqrt(diagonal);
                for (int i = j + 1; i < n; i++)
                {
                    double sum = a[i, j];
                    for (int k = 0; k < j; k++) sum -= a[i, k] * a[j, k];
                    a[i, j] = sum / a[j, j];
                }
            }

            var x = new double[n, m];
            for (int c = 0; c < m; c++)
            {
                // Forward substitution with L, then back substitution with Lᵀ
                for (int i = 0; i < n; i++)
                {
                    double sum = b[i, c];
                    for (int k = 0; k < i; k++) sum -= a[i, k] * x[k, c];
                    x[i, c] = sum / a[i, i];
                }

                for (int i = n - 1; i >= 0; i--)
                {
                    double sum = x[i, c];
                    for (int k = i + 1; k < n; k++) sum -= a[k, i] * x[k, c];
                    x[i, c] = sum / a[i, i];
                }
            }

            return x;
        }
    }
}
