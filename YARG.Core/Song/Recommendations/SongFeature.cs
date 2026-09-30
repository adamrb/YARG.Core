using System;

namespace YARG.Core.Song.Recommendations
{
    public enum FeatureType
    {
        Artist,
        Genre,
        Subgenre,

        /// <summary>
        /// Each word of the genre and subgenre, so "Pop Punk" and "Punk Rock" share "punk".
        /// </summary>
        GenreWord,

        Decade,
        Charter,
        Source,
        Length,

        /// <summary>
        /// Where the song ranks among its artist's songs on the <see cref="SongMap"/>: a hit or a deep cut.
        /// </summary>
        ArtistRank,

        /// <summary>
        /// How many people on the <see cref="SongMap"/> listen to the song.
        /// </summary>
        Listeners,
    }

    /// <summary>
    /// One piece of song metadata the taste model can learn a weight for, such as "Artist: weezer".
    /// </summary>
    public readonly struct SongFeature : IEquatable<SongFeature>
    {
        public readonly FeatureType Type;
        public readonly string Value;

        public SongFeature(FeatureType type, string value)
        {
            Type = type;
            Value = value;
        }

        public bool Equals(SongFeature other) => Type == other.Type && Value == other.Value;
        public override bool Equals(object? obj) => obj is SongFeature other && Equals(other);
        public override int GetHashCode() => HashCode.Combine((int) Type, Value);
        public override string ToString() => $"{Type}: {Value}";
    }
}
