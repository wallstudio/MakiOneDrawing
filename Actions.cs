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
        static readonly string HELP_FILE = "docs/index.md";
        static readonly string HELP_CONFIG_FILE = "docs/_config.yml";
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

        /// <summary>
        /// 朝の予告ツイートを投げる
        /// </summary>
        public void NotificationMorning()
        {
            using var tables = DB.Get(googleServiceAccountJwt, DB_SHEET_ID);

            // Create new schedule
            var schedule = tables["schedule"].Add(ScheduleId);
            var unusedTheme = tables["theme"]
                .Where(thm => !tables["schedule"].Any(ev => ev["id_theme"] == thm["id"]));
            var theme = unusedTheme
                .Where(thm => !DateTime.TryParse(thm["date"], out var d) || d == eventDate.Date) // 別の日を除外
                .OrderByDescending(thm => DateTime.TryParse(thm["date"], out var d) && d == eventDate.Date)
                .First();
            schedule["id_theme"] = theme["id"];
            schedule["date"] = eventDate.ToString("yyyy/MM/dd");

            // Post tweet
            var theme1 = tables["theme"][schedule["id_theme"]]["theme1"];
            var theme2 = tables["theme"][schedule["id_theme"]]["theme2"];
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

        /// <summary>
        /// ワンドロ開始のツイートを投げる
        /// </summary>
        public void NotificationStart()
        {
            using var tables = DB.Get(googleServiceAccountJwt, DB_SHEET_ID);
            var schedule = tables["schedule"][ScheduleId];

            // Post tweet
            var theme1 = tables["theme"][schedule["id_theme"]]["theme1"];
            var theme2 = tables["theme"][schedule["id_theme"]]["theme2"];
            var uploadResult = tokens.Media.Upload(Views.GenerateTextImage($"{theme1}\n\n{theme2}"));
            var start = tokens.Statuses.Update(
                status: Views.StartTweet(theme1, theme2),
                media_ids: new[] { uploadResult.MediaId },
                in_reply_to_status_id: long.Parse(schedule["id_morning_status"]),
                auto_populate_reply_metadata: true);

            // Record
            schedule["id_start_status"] = start.Id.ToString();
            schedule["ts_start_status"] = TimeStamp;
            schedule["ts_utc_start_status"] = TimeStampUtc;
        }

        /// <summary>
        /// ワンドロ終了のツイートを投げる
        /// </summary>
        public void NotificationFinish()
        {
            using var tables = DB.Get(googleServiceAccountJwt, DB_SHEET_ID);
            var schedule = tables["schedule"][ScheduleId];

            // Post tweet
            var finish = tokens.Statuses.Update(
                status: Views.FinishTweet(nextDate),
                in_reply_to_status_id: long.Parse(schedule["id_start_status"]),
                // attachment_url: null, // 引用
                auto_populate_reply_metadata: true);

            // Record
            schedule["id_finish_status"] = finish.Id.ToString();
            schedule["ts_finish_status"] = TimeStamp;
            schedule["ts_utc_finish_status"] = TimeStampUtc;
        }

        /// <summary>
        /// 投稿を集計してRTとランキングを更新する
        /// </summary>
        public void AccumulationPosts()
        {
            using var tables = DB.Get(googleServiceAccountJwt, DB_SHEET_ID);
            var schedule = tables["schedule"][ScheduleId];

            // Collection
            var me = tokens.Account.VerifyCredentials();
            var since = DateTime.Parse(schedule["ts_utc_start_status"]) - TimeSpan.FromMinutes(15); // 15分の遊び
            var until = DateTime.Parse(schedule["ts_utc_finish_status"]) + TimeSpan.FromMinutes(15);
            var tweets = EnumerateSearchTweets(
                q: $"{Views.HASH_TAG} -from:{me.ScreenName} exclude:retweets since:{since:yyy-MM-dd} until:{until:yyy-MM-dd}", // https://gist.github.com/cucmberium/e687e88565b6a9ca7039
                result_type: "recent",
                until: DateTime.UtcNow.ToString("yyy-MM-dd"),
                count: 100,
                include_entities: true,
                tweet_mode: TweetMode.Extended)
                .Where(twt => since <= twt.CreatedAt && twt.CreatedAt <= until)
                .ToArray();

            // Post tweet
            var preRetweet = tokens.Statuses.Update(
                status: Views.ResultTweet(tweets, nextDate),
                in_reply_to_status_id: long.Parse(schedule["id_finish_status"]),
                // attachment_url: null, // 引用
                auto_populate_reply_metadata: true);

            // Record
            schedule["id_accumulation_status"] = preRetweet.Id.ToString();
            schedule["ts_accumulation_status"] = TimeStamp;
            schedule["ts_utc_accumulation_status"] = TimeStampUtc;

            // Twitter
            foreach (var tweet in tweets)
            {
                // TODO:
                // tokens.Favorites.Create(tweet.Id);
                // tokens.Statuses.Retweet(tweet.Id);
                Console.WriteLine($"RT+Fav {tweet.Id,20} {tweet.User.ScreenName,-10} {tweet.Text}");
            }
            var followered = tokens.Friends.EnumerateIds(EnumerateMode.Next, user_id: (long)me.Id, count: 5000).ToArray();
            var noFollowered = tweets.Select(s => s.User).Distinct(UserComparer.Default).Where(u => !followered.Contains(u.Id ?? 0)).ToArray();
            foreach (var user in noFollowered)
            {
                // TODO:
                // tokens.Friendships.Create(user_id: id, follow: true);
                Console.WriteLine($"Follow {user.ScreenName}");
            }

            // Aggregate
            var posts = tables["post"];
            foreach (var tweet in tweets)
            {
                var post = posts.Add(tweet.Id.ToString());
                post["id_status"] = tweet.Id.ToString();
                post["id_schedule"] = schedule["id"];
                post["id_user"] = tweet.User.Id.ToString();
                post["ts_utc_post"] = tweet.CreatedAt.ToString();
                post["user_display_name"] = tweet.User.Name;
                post["user_screen_name"] = tweet.User.ScreenName;
                post["url_user_icon"] = tweet.User.ProfileImageUrlHttps;
                post["url_media"] = tweet.Entities?.Media?.FirstOrDefault()?.MediaUrlHttps;
            }
            var userInfoTable = tokens.Users.Lookup(posts.Select(p => long.Parse(p["id_user"])).Distinct());
            var recently = posts
                .OrderByDescending(pst => DateTime.Parse(pst["ts_utc_post"]))
                .Select(p => new Recentry(userInfoTable.First(u => u.Id == long.Parse(p["id_user"])), p))
                .ToArray();
            var postRanking = posts
                .GroupBy(pst => pst["id_user"])
                .Select(g => new Post(g.Key, userInfoTable.First(u => u.Id == long.Parse(g.Key)), g, g.Count()))
                .OrderBy(info => info.Count)
                .ToArray();
            var entryRanking = posts
                .GroupBy(pst => pst["id_user"])
                .Select(g => new Post(g.Key, userInfoTable.First(u => u.Id == long.Parse(g.Key)), g, g.Select(p => p["id_schedule"]).Distinct().Count()))
                .OrderBy(info => info.Count)
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
                .OrderBy(info => info.Count)
                .ToArray();
            schedule["ranking_post"] = string.Join(",", postRanking.Select(p => p.Id));
            schedule["ranking_entry"] = string.Join(",", entryRanking.Select(p => p.Id));
            schedule["ranking_continue"] = string.Join(",", continueRanking.Select(p => p.Id));

            // Output
            File.WriteAllText(HELP_FILE, Views.Dashboard(me, recently, postRanking, entryRanking, continueRanking), Encoding.UTF8);
            File.WriteAllText(HELP_CONFIG_FILE, SERIALIZER.Serialize(new
            {
                theme = "jekyll-theme-slate",
                title = Views.HASH_TAG,
            }));
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
