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
    /// (<see cref="SortString.SearchStr"/>). The song map build script mirrors <see cref="Artist"/> and <see cref="Title"/>.
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

        // What song loaders write when a field is missing; these say nothing about the song
        private static readonly HashSet<string> Placeholders = new(new[]
        {
            SongMetadata.DEFAULT_NAME, SongMetadata.DEFAULT_ARTIST, SongMetadata.DEFAULT_ALBUM,
            SongMetadata.DEFAULT_CHARTER, SongMetadata.DEFAULT_SOURCE,
        }.Select(Collapse));

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

        /// <summary>
        /// <see cref="Collapse"/>, but empty for placeholders such as "Unknown Artist".
        /// </summary>
        private static string Known(string? text)
        {
            string collapsed = Collapse(text);
            return Placeholders.Contains(collapsed) ? string.Empty : collapsed;
        }

        public static string StripBrackets(string? text)
        {
            var result = new StringBuilder();
            ForEachBracketGroup(text, result, (_, _) => { });
            return result.ToString();
        }

        /// <summary>
        /// The artist without bracketed notes (usually charter credits), a leading "the", or "and", so
        /// "The Beatles" is "beatles" and "Simon &amp; Garfunkel" matches "Simon and Garfunkel".
        /// </summary>
        public static string Artist(string? artist)
        {
            var words = Known(StripBrackets(artist)).Split(' ').Where(word => word != "and").ToList();
            if (words.Count > 1 && words[0] == "the")
            {
                words.RemoveAt(0);
            }

            return string.Join(" ", words);
        }

        /// <summary>
        /// The song's name without version notes: "(Live)", "(2005 Prototype)" and "[Remastered]" go,
        /// while "(Slight Return)" or "(Parts I-V)" stay, since they are part of the name.
        /// </summary>
        public static string Title(string? title)
        {
            var result = new StringBuilder();
            ForEachBracketGroup(SplitDashNotes(title).Name, result, (group, output) =>
            {
                if (!IsVersionNote(group))
                {
                    output.Append(' ').Append(group).Append(' ');
                }
            });
            return Known(result.ToString());
        }

        /// <summary>
        /// One string shared by every chart of the same song, such as a Harmonix and a Neversoft chart, or
        /// empty when the artist or title is missing (the song then stands alone, see <see cref="SongFacts.Identity"/>).
        /// </summary>
        public static string Identity(string? artist, string? title)
        {
            string artistKey = Artist(artist), titleKey = Title(title);
            return artistKey.Length > 0 && titleKey.Length > 0 ? artistKey + "|" + titleKey : string.Empty;
        }

        /// <summary>
        /// True when the title marks this chart as a demo, prototype, live take or similar.
        /// </summary>
        public static bool IsAlternateVersion(string? title)
        {
            if (SplitDashNotes(title).Notes.Any(note => Collapse(note).Split(' ').Any(AlternateVersionWords.Contains)))
            {
                return true;
            }

            bool alternate = false;
            ForEachBracketGroup(title, new StringBuilder(), (group, _) =>
                alternate |= Collapse(group).Split(' ').Any(AlternateVersionWords.Contains));
            return alternate;
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

            Add(FeatureType.Charter, Known(charter));
            Add(FeatureType.Source, Known(source));
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

        /// <summary>
        /// Splits off the version notes streaming services write after dashes: "Song - Live - 2011 Remaster"
        /// is "Song" with notes "2011 Remaster" and "Live". Dashes that are part of the name stay.
        /// </summary>
        private static (string Name, List<string> Notes) SplitDashNotes(string? title)
        {
            string name = title ?? string.Empty;
            var notes = new List<string>();
            for (int dash = name.LastIndexOf(" - ", StringComparison.Ordinal);
                dash > 0 && IsVersionNote(name.Substring(dash + 3));
                dash = name.LastIndexOf(" - ", StringComparison.Ordinal))
            {
                notes.Add(name.Substring(dash + 3));
                name = name.Substring(0, dash);
            }

            return (name, notes);
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
