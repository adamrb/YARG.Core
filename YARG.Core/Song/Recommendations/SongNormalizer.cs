using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace YARG.Core.Song.Recommendations
{
    /// <summary>
    /// Turns song metadata into model features, smoothing over the inconsistencies of large custom
    /// libraries: "Pop/Rock", "Pop Rock" and "pop-rock" are one genre, "Artist (Charter Name)" is the
    /// artist, and "Song (2005 Prototype)" is the same song as "Song".
    /// </summary>
    /// <remarks>
    /// Callers should pass text with rich-text tags and diacritics already removed
    /// (<see cref="SortString.SearchStr"/>). The artist map build script mirrors <see cref="Artist"/>.
    /// </remarks>
    public static class SongNormalizer
    {
        private static readonly HashSet<string> GenreStopWords = new() { "and", "n", "the", "other", "of" };

        // Words marking a chart as a non-definitive take of a song
        private static readonly HashSet<string> AlternateVersionWords = new()
        {
            "demo", "prototype", "beta", "live", "remix", "rehearsal", "alt", "alternate", "cover", "karaoke",
        };

        // Words that make a bracketed part of a title a version note rather than part of the name
        private static readonly HashSet<string> VersionNoteWords = new(AlternateVersionWords.Concat(new[]
        {
            "version", "remaster", "remastered", "mix", "edit", "radio", "single", "album", "feat", "ft",
            "featuring", "take", "retail", "early", "original", "rerecord", "rerecorded", "mono", "stereo",
            "acoustic", "unplugged", "instrumental", "extended", "short", "full", "jan", "feb", "mar", "apr",
            "may", "jun", "jul", "aug", "sep", "sept", "oct", "nov", "dec",
        }));

        /// <summary>
        /// Lower case, with every run of non-alphanumeric characters turned into a single space.
        /// </summary>
        public static string Collapse(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }

            var chars = text!.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : ' ').ToArray();
            return string.Join(" ", new string(chars).Split(' ', StringSplitOptions.RemoveEmptyEntries));
        }

        public static string StripBrackets(string? text)
        {
            var result = new StringBuilder();
            ForEachBracketGroup(text, result, (_, _) => { });
            return result.ToString();
        }

        /// <summary>
        /// The artist without bracketed notes (usually charter credits) and without a leading "the".
        /// </summary>
        public static string Artist(string? artist)
        {
            string name = Collapse(StripBrackets(artist));
            return name.StartsWith("the ") ? name.Substring(4) : name;
        }

        /// <summary>
        /// The song's name without version notes: "(Live)", "(2005 Prototype)" and "[Remastered]" go,
        /// while "(Slight Return)" or "(Parts I-V)" stay, since they are part of the name.
        /// </summary>
        public static string Title(string? title)
        {
            var result = new StringBuilder();
            ForEachBracketGroup(title, result, (group, output) =>
            {
                if (!IsVersionNote(group))
                {
                    output.Append(' ').Append(group).Append(' ');
                }
            });
            return Collapse(result.ToString());
        }

        /// <summary>
        /// One string shared by every chart of the same song, such as a Harmonix and a Neversoft chart.
        /// </summary>
        public static string Identity(string? artist, string? title) => Artist(artist) + "|" + Title(title);

        /// <summary>
        /// True when the title marks this chart as a demo, prototype, live take or similar.
        /// </summary>
        public static bool IsAlternateVersion(string? title)
        {
            var bracketed = Collapse(title).Split(' ').Except(Collapse(StripBrackets(title)).Split(' '));
            return bracketed.Any(AlternateVersionWords.Contains);
        }

        public static SongFeature[] Features(string? artist, string? genre, string? subgenre, string? charter,
            string? source, int year, double lengthSeconds)
        {
            var features = new List<SongFeature>(12);

            void Add(FeatureType type, string value)
            {
                if (value.Length > 0)
                {
                    features.Add(new SongFeature(type, value));
                }
            }

            string genreText = Collapse(genre);
            string subgenreText = Collapse(subgenre);
            Add(FeatureType.Artist, Artist(artist));
            Add(FeatureType.Genre, genreText);
            Add(FeatureType.Subgenre, subgenreText);
            foreach (string word in (genreText + " " + subgenreText).Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Where(w => !GenreStopWords.Contains(w))
                .Distinct())
            {
                Add(FeatureType.GenreWord, word);
            }

            Add(FeatureType.Charter, Collapse(charter));
            Add(FeatureType.Source, Collapse(source));
            if (year > 0 && year < 3000)
            {
                Add(FeatureType.Decade, (year / 10 * 10).ToString());
            }

            if (lengthSeconds > 0)
            {
                Add(FeatureType.Length, lengthSeconds switch
                {
                    < 150 => "short",
                    < 270 => "medium",
                    < 420 => "long",
                    _     => "epic",
                });
            }

            return features.ToArray();
        }

        /// <summary>
        /// Marks one chart of each song as canonical: a playable chart first, then one that is not a demo
        /// or prototype, then the shortest title.
        /// </summary>
        public static void AssignCanonical(IEnumerable<(SongFacts Facts, string Title)> songs)
        {
            foreach (var group in songs.GroupBy(s => s.Facts.Identity))
            {
                var best = group
                    .OrderBy(s => s.Facts.ChartDifficulty.HasValue ? 0 : 1)
                    .ThenBy(s => IsAlternateVersion(s.Title) ? 1 : 0)
                    .ThenBy(s => s.Title.Length)
                    .First();
                foreach (var song in group)
                {
                    song.Facts.Canonical = ReferenceEquals(song.Facts, best.Facts);
                }
            }
        }

        private static bool IsVersionNote(string text)
        {
            return Collapse(text).Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Any(w => VersionNoteWords.Contains(w) || (w.Length == 4 && w.All(char.IsDigit)));
        }

        /// <summary>
        /// Copies text outside brackets to <paramref name="output"/> and hands each top-level bracket
        /// group's contents to <paramref name="onGroup"/>.
        /// </summary>
        private static void ForEachBracketGroup(string? text, StringBuilder output, Action<string, StringBuilder> onGroup)
        {
            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            var group = new StringBuilder();
            int depth = 0;
            foreach (char c in text!)
            {
                if (c is '(' or '[' or '{')
                {
                    if (depth++ == 0) group.Clear();
                }
                else if (c is ')' or ']' or '}')
                {
                    if (depth > 0 && --depth == 0) onGroup(group.ToString(), output);
                }
                else
                {
                    (depth == 0 ? output : group).Append(c);
                }
            }
        }
    }
}
