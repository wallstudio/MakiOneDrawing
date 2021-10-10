using System;
using System.Linq;
using CoreTweet;
using System.IO;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Text;
using YamlDotNet.Serialization;

namespace MakiOneDrawingBot
{
    class Actions
    {
        static readonly string DB_SHEET_ID = "1Un15MnW9Z2ChwSdsxdAVw495uSmJN4jBHngcBpYxo_0";
        static readonly string DOCS_DIR = "docs";
        static readonly string HELP_CONFIG_FILE = $"{DOCS_DIR}/_config.yml";
        static readonly Serializer SERIALIZER = new();
        readonly string googleServiceAccountJwt;
        readonly DateTime eventDate;
        readonly DateTime? nextDate;
        readonly string general;
        readonly Tokens tokens;

        string ScheduleId => eventDate.ToString("yyyy_MM_dd");
        string TimeStamp => (DateTime.UtcNow + TimeSpan.FromHours(+9)).ToString();
        string TimeStampUtc => DateTime.UtcNow.ToString();

        public Actions(string twitterApiKey, string twitterApiSecret, string bearerToken, string accessToken, string accessTokenSecret, string googleServiceAccountJwt, string date, string next, string general)
        {
            this.googleServiceAccountJwt = Encoding.UTF8.GetString(Convert.FromBase64String(googleServiceAccountJwt));
            tokens = Tokens.Create(twitterApiKey, twitterApiSecret, accessToken, accessTokenSecret);
            eventDate = DateTime.Parse(date);
            nextDate = DateTime.TryParse(next, out var d) ? d : null;
            this.general = general;
        }

        public IEnumerable<(string text, byte[] bin)> TestGenerateTextImage()
        {
            using var tables = DB.Get(googleServiceAccountJwt, DB_SHEET_ID);
            foreach (var theme in tables["theme"].Where(thm => !string.IsNullOrEmpty(thm["id"])))
            {
                var text = $"{theme["theme1"]}\n\n{theme["theme2"]}";
                yield return (text, Views.GenerateTextImage(text));
            }
        }

        /// <summary> 朝の予告ツイートを投げる </summary>
        public void NotificationMorning()
        {
            using var tables = DB.Get(googleServiceAccountJwt, DB_SHEET_ID);
            var schedule = CreateOrGetSchedule(tables);

            // Post tweet
            var themeId = schedule["id_theme"];
            var theme1 = tables["theme"][themeId]["theme1"];
            var theme2 = tables["theme"][themeId]["theme2"];
            var uploadResult = tokens.Media.Upload(Views.GenerateTextImage($"{theme1}\n\n{theme2}"));
            var morning = tokens.Statuses.Update(
                status: Views.PredictTweet(theme1, theme2),
                media_ids: new[] { uploadResult.MediaId },
                auto_populate_reply_metadata: true);

            // Record
            schedule["id_morning_status"] = morning.Id.ToString();
            schedule["ts_morning_status"] = TimeStamp;
            schedule["ts_utc_morning_status"] = TimeStampUtc;
        }

        /// <summary> ワンドロ開始のツイートを投げる </summary>
        public void NotificationStart()
        {
            using var tables = DB.Get(googleServiceAccountJwt, DB_SHEET_ID);
            var schedule = CreateOrGetSchedule(tables);

            // Post tweet
            var themeId = schedule["id_theme"];
            var theme1 = tables["theme"][themeId]["theme1"];
            var theme2 = tables["theme"][themeId]["theme2"];
            var uploadResult = tokens.Media.Upload(Views.GenerateTextImage($"{theme1}\n\n{theme2}"));
            var start = tokens.Statuses.Update(
                status: Views.StartTweet(theme1, theme2),
                media_ids: new[] { uploadResult.MediaId },
                in_reply_to_status_id: long.TryParse(schedule["id_morning_status"], out var i) ? i : null,
                auto_populate_reply_metadata: true);

            // Record
            schedule["id_start_status"] = start.Id.ToString();
            schedule["ts_start_status"] = TimeStamp;
            schedule["ts_utc_start_status"] = TimeStampUtc;
        }

        /// <summary> ワンドロ終了のツイートを投げる </summary>
        public void NotificationFinish()
        {
            using var tables = DB.Get(googleServiceAccountJwt, DB_SHEET_ID);
            var schedule = CreateOrGetSchedule(tables);

            // Post tweet
            var finish = tokens.Statuses.Update(
                status: Views.FinishTweet(nextDate),
                in_reply_to_status_id: long.TryParse(schedule["id_start_status"], out var i) ? i : null,
                // attachment_url: null, // 引用
                auto_populate_reply_metadata: true);

            // Record
            schedule["id_finish_status"] = finish.Id.ToString();
            schedule["ts_finish_status"] = TimeStamp;
            schedule["ts_utc_finish_status"] = TimeStampUtc;
        }

        /// <summary> 投稿を集計してRTとランキングを更新する </summary>
        public void AccumulationPosts()
        {
            using var tables = DB.Get(googleServiceAccountJwt, DB_SHEET_ID);
            var schedule = CreateOrGetSchedule(tables);

            // Collection
            var me = tokens.Account.VerifyCredentials();
            var since = DateTime.Parse(schedule["ts_start_status"]) - TimeSpan.FromHours(3);
            var until = DateTime.Parse(schedule["ts_finish_status"]) + TimeSpan.FromHours(3); // 遅刻OK
            var format = @"yyy-MM-dd_HH\:mm\:ss_JST";
            var query = schedule["query"] = $"{Views.HASH_TAG} -from:{me.ScreenName} exclude:retweets since:{since.ToString(format)} until:{until.ToString(format)}"; // https://gist.github.com/cucmberium/e687e88565b6a9ca7039
            var tweets = EnumerateSearchTweets(
                q: query,
                result_type: "recent",
                until: DateTime.UtcNow.ToString("yyy-MM-dd"),
                count: 100,
                include_entities: true,
                tweet_mode: TweetMode.Extended)
                // .Where(twt => since <= twt.CreatedAt && twt.CreatedAt <= until)
                .ToArray();

            // Post tweet
            var preRetweet = tokens.Statuses.Update(
                status: Views.ResultTweet(tweets, nextDate),
                in_reply_to_status_id: long.TryParse(schedule["id_finish_status"], out var i) ? i : null,
                // attachment_url: null, // 引用
                auto_populate_reply_metadata: true);

            // Record
            schedule["id_accumulation_status"] = preRetweet.Id.ToString();
            schedule["ts_accumulation_status"] = TimeStamp;
            schedule["ts_utc_accumulation_status"] = TimeStampUtc;

            // Reflect Twitter
            foreach (var tweet in tweets)
            {
                tokens.Favorites.Create(tweet.Id);
                tokens.Statuses.Retweet(tweet.Id);
                Console.WriteLine($"RT+Fav {tweet.Id,20} {tweet.User.ScreenName,-10} {tweet.Text}");
            }
            var followered = tokens.Friends.EnumerateIds(EnumerateMode.Next, user_id: (long)me.Id, count: 5000).ToArray();
            var noFollowered = tweets.Select(s => s.User).Distinct(UserComparer.Default).Where(u => !followered.Contains(u.Id ?? 0)).ToArray();
            foreach (var user in noFollowered)
            {
                tokens.Friendships.Create(user_id: user.Id.Value, follow: true);
                Console.WriteLine($"Follow {user.ScreenName}");
            }

            // Reflect DB
            foreach (var tweet in tweets)
            {
                var post = tables["post"].Add(tweet.Id.ToString());
                post["id_status"] = tweet.Id.ToString();
                post["id_schedule"] = schedule["id"];
                post["id_user"] = tweet.User.Id.ToString();
                post["ts_utc_post"] = tweet.CreatedAt.ToString();
                post["user_display_name"] = tweet.User.Name;
                post["user_screen_name"] = tweet.User.ScreenName;
                post["url_user_icon"] = tweet.User.ProfileImageUrlHttps;
                post["url_media"] = tweet.Entities?.Media?.FirstOrDefault()?.MediaUrlHttps;
            }
            Console.WriteLine($"Total post: {tables["post"].Count()} Total Users: {tables["post"].Select(p => long.Parse(p["id_user"])).Distinct().Count()}");

            RegeneratSummaryPage(tables, me);
        }

        public void RegeneratSummaryPage()
        {
            using var tables = DB.Get(googleServiceAccountJwt, DB_SHEET_ID);
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

        (Recentry[] recently, Post[] postRanking, Post[] entryRanking, Post[] continueRanking) Aggregate(DB tables)
        {
            var posts = tables["post"];
            var userInfoTable = posts
                .Select(p => long.Parse(p["id_user"]))
                .Distinct()
                .Select((id, i) => (id, i))
                // .SelectMany(ids => tokens.Users.Lookup(ids))
                .GroupBy(t => t.i / 95, t => t.id).SelectMany(ids => tokens.Users.Lookup(ids)) // avoid limit
                .ToArray();
            var recently = posts
                .OrderByDescending(pst => DateTime.Parse(tables["schedule"][pst["id_schedule"]]["date"]))
                .ThenBy(pst => DateTime.Parse(pst["ts_utc_post"]))
                .Select(p => new Recentry(userInfoTable.First(u => u.Id == long.Parse(p["id_user"])), p))
                .ToArray();
            var postRanking = posts
                .GroupBy(pst => pst["id_user"])
                .Select(g => new Post(g.Key, userInfoTable.First(u => u.Id == long.Parse(g.Key)), g, g.Count()))
                .OrderByDescending(info => info.Count)
                .ThenBy(post => post.User.ScreenName == "yukawallstudio")
                .ToArray();
            var entryRanking = posts
                .GroupBy(pst => pst["id_user"])
                .Select(g => new Post(g.Key, userInfoTable.First(u => u.Id == long.Parse(g.Key)), g, g.Select(p => p["id_schedule"]).Distinct().Count()))
                .OrderByDescending(info => info.Count)
                .ThenBy(post => post.User.ScreenName == "yukawallstudio")
                .ToArray();
            var continueRanking = posts
                .GroupBy(pst => pst["id_user"])
                .Select(g => new Post(
                    Id: g.Key,
                    User: userInfoTable.First(u => u.Id == long.Parse(g.Key)),
                    Posts: g,
                    Count: tables["schedule"]
                .OrderByDescending(s => DateTime.Parse(s["date"]))
                .TakeWhile(s => g.Any(pst => pst["id_schedule"] == s["id"]))
                .Count()))
                .OrderByDescending(info => info.Count)
                .ThenBy(post => post.User.ScreenName == "yukawallstudio")
                .ToArray();
            return (recently, postRanking, entryRanking, continueRanking);
        }

        Entry CreateOrGetSchedule(DB tables)
        {
            if(tables["schedule"].TryGet(ScheduleId, out var schedule))
            {
                return schedule;
            }

            schedule = tables["schedule"].Add(ScheduleId);
            var unusedTheme = tables["theme"]
                .Where(thm => !tables["schedule"].Any(ev => ev["id_theme"] == thm["id"]));
            var theme = unusedTheme
                .Where(thm => !DateTime.TryParse(thm["date"], out var d) || d == eventDate.Date) // 別の日を除外
                .OrderByDescending(thm => DateTime.TryParse(thm["date"], out var d) && d == eventDate.Date)
                .First();
            schedule["id_theme"] = theme["id"];
            schedule["date"] = eventDate.ToString("yyyy/MM/dd");
            Console.WriteLine($"created new schedule {ScheduleId}");
            return schedule;
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
