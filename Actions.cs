using System;
using System.Linq;
using CoreTweet;
using System.IO;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Text;
using YamlDotNet.Serialization;
using MakiOneDrawingBot.Entries;

namespace MakiOneDrawingBot
{
    class Actions
    {
        static readonly string DB_SHEET_ID = "1Un15MnW9Z2ChwSdsxdAVw495uSmJN4jBHngcBpYxo_0";
        static readonly string DOCS_DIR = "docs";
        static readonly string HELP_CONFIG_FILE = $"{DOCS_DIR}/_config.yml";
        static readonly Serializer SERIALIZER = new();
        readonly string googleServiceAccountJwt;
        readonly DateOnly eventDate;
        readonly string general;
        readonly Tokens tokens;

        bool DryMode => general?.Contains("DRYMODE") ?? false;
        string ScheduleId => eventDate.ToString("yyyy/MM/dd");

        public Actions(string twitterApiKey, string twitterApiSecret, string bearerToken, string accessToken, string accessTokenSecret, string googleServiceAccountJwt, string date, string general)
        {
            this.googleServiceAccountJwt = Encoding.UTF8.GetString(Convert.FromBase64String(googleServiceAccountJwt));
            tokens = Tokens.Create(twitterApiKey, twitterApiSecret, accessToken, accessTokenSecret);
            eventDate = DateOnly.Parse(date);
            this.general = general;
        }

        public IEnumerable<(string text, byte[] bin)> TestGenerateTextImages()
        {
            using var db = new DB(googleServiceAccountJwt, DB_SHEET_ID, writable: false);
            var images = db.GetTable<Schedule>()
                .AsParallel()
                .Select(schedule =>
                {
                    var text = $"{schedule.Theme1}\n\n{schedule.Theme2}";
                    return (text, Views.GenerateTextImage(text));
                })
                .ToArray();
            return images;
        }

        /// <summary> 朝の予告ツイートを投げる </summary>
        public void NotificationMorning()
        {
            using var db = new DB(googleServiceAccountJwt, DB_SHEET_ID, writable: !DryMode);
            var schedule = db.GetTable<Schedule>()[ScheduleId];
            if(schedule is null) throw new Exception($"Not fond schedule entry. {ScheduleId}");

            var uploadResult = tokens.Media.Upload(Views.GenerateTextImage($"{schedule.Theme1}\n\n{schedule.Theme2}"));
            var morning = tokens.Statuses.Update(
                status: Views.PredictTweet(schedule.Theme1, schedule.Theme2),
                media_ids: new[] { uploadResult.MediaId },
                auto_populate_reply_metadata: true);

            schedule.PreId = morning.Id;
        }

        /// <summary> ワンドロ開始のツイートを投げる </summary>
        public void NotificationStart()
        {
            using var db = new DB(googleServiceAccountJwt, DB_SHEET_ID, writable: !DryMode);
            var schedule = db.GetTable<Schedule>()[ScheduleId];
            if(schedule is null) throw new Exception($"Not fond schedule entry. {ScheduleId}");

            var uploadResult = tokens.Media.Upload(Views.GenerateTextImage($"{schedule.Theme1}\n\n{schedule.Theme2}"));
            var start = tokens.Statuses.Update(
                status: Views.StartTweet(schedule.Theme1, schedule.Theme2),
                media_ids: new[] { uploadResult.MediaId },
                in_reply_to_status_id: schedule.PreId,
                auto_populate_reply_metadata: true);

            schedule.BeginId = start.Id;
        }

        /// <summary> ワンドロ終了のツイートを投げる </summary>
        public void NotificationFinish()
        {
            using var db = new DB(googleServiceAccountJwt, DB_SHEET_ID, writable: !DryMode);
            var schedule = db.GetTable<Schedule>()[ScheduleId];
            if(schedule is null) throw new Exception($"Not fond schedule entry. {ScheduleId}");

            var next = DateOnly.Parse(db.GetTable<Schedule>().SkipWhile(s => s.Id != ScheduleId).Skip(1).First().Id);
            var finish = tokens.Statuses.Update(
                status: Views.FinishTweet(next),
                in_reply_to_status_id: schedule.BeginId,
                // attachment_url: null, // 引用
                auto_populate_reply_metadata: true);

            schedule.EndId = finish.Id;
        }

        /// <summary> 投稿を集計してRTとランキングを更新する </summary>
        public void AccumulationPosts()
        {
            using var db = new DB(googleServiceAccountJwt, DB_SHEET_ID, writable: !DryMode);
            var schedule = db.GetTable<Schedule>()[ScheduleId];
            if(schedule is null) throw new Exception($"Not fond schedule entry. {ScheduleId}");

            // Collection
            var me = tokens.Account.VerifyCredentials();
            var since = tokens.Statuses.Lookup(new[] { (long)schedule.BeginId })[0].CreatedAt - TimeSpan.FromHours(3);
            var until = tokens.Statuses.Lookup(new[] { (long)schedule.EndId })[0].CreatedAt + TimeSpan.FromHours(3); // 遅刻OK
            var format = @"yyy-MM-dd_HH\:mm\:ss_UTC";
            var query = $"{Views.HASH_TAG} -from:{me.ScreenName} exclude:retweets since:{since.ToString(format)} until:{until.ToString(format)}"; // https://gist.github.com/cucmberium/e687e88565b6a9ca7039
            var foundTweets = EnumerateSearchTweets(
                q: query,
                result_type: "recent",
                until: DateTime.UtcNow.ToString("yyy-MM-dd"),
                count: 100,
                include_entities: true,
                tweet_mode: TweetMode.Extended)
                // .Where(twt => since <= twt.CreatedAt && twt.CreatedAt <= until)
                .ToArray();
            Console.WriteLine($"Queried {foundTweets.Length} <- `{query}`");
                
            // Reflect DB
            var posts = db.GetTable<Post>();
            foreach (var tweet in foundTweets)
            {
                if(posts.Any(p => p.Id == tweet.Id.ToString())) continue;

                posts[tweet.Id.ToString()] = new Post()
                {
                    Id = tweet.Id.ToString(),
                    ScheduleId = schedule.Id,
                    UserName = tweet.User.Name.ToString(),
                };
            }
            RegeneratSummaryPage(db, me);
            Console.WriteLine($"Total post: {posts.Count()}");

            // Post tweet
            var newTweets = posts.Where(p => p.ScheduleId == schedule.Id).ToArray();
            var next = DateOnly.Parse(db.GetTable<Schedule>().SkipWhile(s => s.Id != ScheduleId).Skip(1).First().Id);
            var preRetweet = DryMode
                ? null
                : tokens.Statuses.Update(
                    status: Views.ResultTweet(newTweets, next),
                    in_reply_to_status_id: schedule.EndId,
                    // attachment_url: null, // 引用
                    auto_populate_reply_metadata: true);
            schedule.AccId = preRetweet?.Id;

            // Reflect Twitter
            foreach (var tweet in newTweets)
            {
                if(!DryMode)
                {
                    tokens.Favorites.Create(long.Parse(tweet.Id));
                    tokens.Statuses.Retweet(long.Parse(tweet.Id));
                }
                Console.WriteLine($"RT+Fav {tweet.Id,20}");
            }
            
            var allUsers = posts
                .Select(p => long.Parse(p.Id))
                .Chunk(95).SelectMany(ts => tokens.Statuses.Lookup(ts)) // avoid limit
                .Select(s => (long)s.User.Id)
                .Distinct();
            var followered = tokens.Friends.EnumerateIds(EnumerateMode.Next, user_id: (long)me.Id, count: 5000).ToArray();
            var newUsers = allUsers.Except(followered);
            foreach (var user in newUsers)
            {
                if(!DryMode)
                {
                    tokens.Friendships.Create(user_id: user, follow: true);
                }
                Console.WriteLine($"Follow {user}");
            }
        }

        public void RegeneratSummaryPage()
        {
            using var tables = new DB(googleServiceAccountJwt, DB_SHEET_ID, writable: false);
            var me = tokens.Account.VerifyCredentials();
            RegeneratSummaryPage(tables, me);
        }

        void RegeneratSummaryPage(DB tables, User me)
        {
            // Aggregate
            var (recently, postRanking, entryRanking, continueRanking) = Aggregate(tables);

            // Output
            File.WriteAllText($"{DOCS_DIR}/{Views.HELP_URL_INDEX}.md", Views.Dashboard(recently, postRanking, entryRanking, continueRanking), Encoding.UTF8);
            File.WriteAllText($"{DOCS_DIR}/{Views.HELP_URL_RECENTRY}.md", Views.RecentryPage(recently), Encoding.UTF8);
            File.WriteAllText($"{DOCS_DIR}/{Views.HELP_URL_POST_RANK}.md", Views.PostRankingPage(postRanking), Encoding.UTF8);
            File.WriteAllText($"{DOCS_DIR}/{Views.HELP_URL_ENTRY_RANK}.md", Views.EntryRankingPage(entryRanking), Encoding.UTF8);
            File.WriteAllText($"{DOCS_DIR}/{Views.HELP_URL_CONTINUE_RANK}.md", Views.ContinueRankingPage(continueRanking), Encoding.UTF8);
            File.WriteAllText(HELP_CONFIG_FILE, SERIALIZER.Serialize(new
            {
                theme = "jekyll-theme-slate",
                title = Views.HASH_TAG,
            }));
        }

        (Views.Recentry[] recently, Views.Post[] postRanking, Views.Post[] entryRanking, Views.Post[] continueRanking) Aggregate(DB tables)
        {
            var posts = tables.GetTable<Post>();
            var schedule = tables.GetTable<Schedule>();
            var tweets = posts
                .Select(p => long.Parse(p.Id))
                .Chunk(95).SelectMany(ts => tokens.Statuses.Lookup(ts, tweet_mode: TweetMode.Extended)) // avoid limit
                .ToDictionary(s => posts.First(p => s.Id.ToString() == p.Id));

            var recently = tweets
                .OrderByDescending(t => DateOnly.Parse(t.Key.ScheduleId))
                .ThenBy(t => t.Value.CreatedAt)
                .Select(t => new Views.Recentry()
                {
                    Post = t.Key,
                    Status = t.Value,
                })
                .ToArray();
            var postRanking = tweets.Values
                .GroupBy(t => t.User.Id)
                .Select(g => new Views.Post()
                {
                    User = g.First().User,
                    Count = g.Count()
                })
                .OrderByDescending(post => post.Count)
                .ThenBy(post => post.User.ScreenName == "yukawallstudio")
                .ThenBy(post => post.User.Id)
                .ToArray();
            var entryRanking = tweets
                .GroupBy(t => t.Value.User.Id)
                .Select(g => new Views.Post()
                {
                    User = g.First().Value.User,
                    Count = g.DistinctBy(p => p.Key.ScheduleId).Count(),
                })
                .OrderByDescending(post => post.Count)
                .ThenBy(post => post.User.ScreenName == "yukawallstudio")
                .ThenBy(post => post.User.Id)
                .ToArray();
            var continueRanking = tweets
                .GroupBy(t => t.Value.User.Id)
                .Select(g => new Views.Post()
                {
                    User = g.First().Value.User,
                    Count = schedule
                        .Where(s => s.AccId is not null)
                        .Reverse()
                        .TakeWhile(s => g.Any(t => t.Key.ScheduleId == s.Id))
                        .Count(),
                })
                .OrderByDescending(post => post.Count)
                .ThenBy(post => post.User.ScreenName == "yukawallstudio")
                .ThenBy(post => post.User.Id)
                .ToArray();
            return (recently, postRanking, entryRanking, continueRanking);
        }

        IEnumerable<Status> EnumerateSearchTweets(string q, string geocode = null, string lang = null, string locale = null, string result_type = null, int? count = null, string until = null, long? since_id = null, long? max_id = null, bool? include_entities = null, bool? include_ext_alt_text = null, TweetMode? tweet_mode = null)
        {
            do
            {
                var r = tokens.Search.Tweets(q, geocode, lang, locale, result_type, count, until, since_id, max_id, include_entities, include_ext_alt_text, tweet_mode);
                max_id = string.IsNullOrEmpty(r.SearchMetadata.NextResults) ? null
                    : long.Parse(Regex.Match(r.SearchMetadata.NextResults, $"{nameof(max_id)}=(?<{nameof(max_id)}>[0-9]+)").Groups[$"{nameof(max_id)}"].Value);
                foreach (var s in r) yield return s;
            }
            while(max_id != null);
        }

    }

    class UserComparer : IEqualityComparer<User>
    {
        public static readonly IEqualityComparer<User> Default = new UserComparer();
        public bool Equals(User x, User y) => x.Id == y.Id;
        public int GetHashCode(User obj) => obj.Id?.GetHashCode() ?? 0;
    }

}
