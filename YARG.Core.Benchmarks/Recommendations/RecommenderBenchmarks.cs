using System;
using System.Collections.Generic;
using System.Linq;
using BenchmarkDotNet.Attributes;
using YARG.Core.Song.Recommendations;

namespace YARG.Core.Benchmarks
{
    /// <summary>
    /// The recommender on a large library with artist-map positions and a long history: 8,000 songs,
    /// 2,000 plays over 700 songs and 500 swipes.
    /// </summary>
    public class RecommenderBenchmarks
    {
        private Dictionary<string, SongFacts> _library;
        private ProfileHistory _history;
        private TasteModel _taste;
        private SkillModel _skill;

        [GlobalSetup]
        public void Initialize()
        {
            string[] genres = { "Rock", "Metal", "Pop", "Punk", "Country", "Jazz", "Electronic", "Indie", "Alternative", "Hip Hop" };
            var random = new Random(9);
            var now = DateTime.Now;
            _library = new Dictionary<string, SongFacts>();
            for (int i = 0; i < 8000; i++)
            {
                string key = $"s{i}";
                string artist = $"artist{random.Next(1000)}";
                _library[key] = new SongFacts
                {
                    Key = key,
                    Features = SongNormalizer.Features(artist, genres[random.Next(genres.Length)], null, "charter",
                        "source", 1960 + 10 * random.Next(7), 200),
                    ChartDifficulty = SkillModel.ChartDifficulty(random.Next(7), Difficulty.Expert),
                    Identity = SongNormalizer.Identity(artist, key),
                    Position = Enumerable.Range(0, 32).Select(_ => (float) random.NextDouble() - 0.5f).ToArray(),
                };
            }

            var songs = _library.Values.ToList();
            _history = new ProfileHistory
            {
                Plays = Enumerable.Range(0, 2000).Select(i => new PlayFact
                {
                    Key = songs[i % 700].Key,
                    Date = now.AddDays(-(i % 90)),
                    Accuracy = 0.9f,
                    OnCurrentInstrument = true,
                    OnCurrentDifficulty = true,
                    ChartDifficulty = songs[i % 700].ChartDifficulty,
                }).ToList(),
                Feedback = songs.Skip(700).Take(500)
                    .Select((s, i) => new FeedbackFact { Key = s.Key, Liked = i % 3 != 0, Date = now.AddMinutes(i) })
                    .ToList(),
                Now = now,
            };
            _taste = TasteModel.Build(_library, _history);
            _skill = SkillModel.Fit(_history);
        }

        [Benchmark]
        public TasteModel TrainTasteModel() => TasteModel.Build(_library, _history);

        [Benchmark]
        public List<RecommendedSong> Recommend() =>
            Recommender.Recommend(_library, _history, _taste, _skill, new Random(9));

        [Benchmark]
        public List<SongFacts> PickSwipeCards() =>
            Recommender.PickSwipeCards(_library, _taste, new HashSet<string>(), 10, new Random(9));

        [Benchmark]
        public List<(string Key, bool Liked, float Surprise)> RankLikelyMistakes() =>
            Recommender.RankLikelyMistakes(_library, _history);
    }
}
