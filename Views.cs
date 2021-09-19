
using System.Linq;
using CoreTweet;
using System.Collections.Generic;
using System;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;
using SixLabors.Fonts;
using SixLabors.ImageSharp.Drawing.Processing;
using System.IO;
using System.Text;

namespace MakiOneDrawingBot
{
    
    static class Views
    {
        public static readonly string HASH_TAG = "#弦巻マキ深夜の真剣お絵描き60分勝負";
        static string HELP_URL => $"https://wallstudio.github.io/MakiOneDrawing?v={DateTime.Now.Ticks:x}";
        public static string HELP_URL_INDEX => $"index";
        public static string HELP_URL_RECENTRY => $"recentry";
        public static string HELP_URL_POST_RANK => $"post_rank";
        public static string HELP_URL_ENTRY_RANK => $"entry_rank";
        public static string HELP_URL_CONTINUE_RANK => $"continue_rank";
        
        public static string PredictTweet(string theme1, string theme2)
        {
            var text = $@"
{HASH_TAG} #ツルマキマキ
今夜のわんどろのテーマ発表！

今回のお題はこちらの二つ！
「{theme1}」
「{theme2}」

▼イベントルール詳細
{HELP_URL}
            ";
            return text.Trim();
        }

        public static string StartTweet(string theme1, string theme2)
        {
            var text = $@"
{HASH_TAG} #ツルマキマキ
わんどろスタート！(｀・ω・´）

今回のお題はこちらの二つ！
投稿時はタグを忘れないでくださいね！！
「{theme1}」
「{theme2}」

▼イベントルール詳細
{HELP_URL}
            ";
            return text.Trim();
        }

        public static string FinishTweet(DateTime? nextDate)
        {
            var text = $@"
{HASH_TAG} #ツルマキマキ
わんどろ終了ーー！！( ´ ∀`)ﾉA

投稿いただいたイラストは明日のお昼にRTします！！
次回は {nextDate?.ToString(@"MM/dd\(ddd\)") ?? "未定"} の予定です、お楽しみに！！

▼イベントルール詳細
{HELP_URL}
            ";
            return text.Trim();
        }

        public static string ResultTweet(Status[] tweets, DateTime? nextDate)
        {
            string text;
            if(tweets.Length > 0)
            {
                text = $@"
{HASH_TAG} #ツルマキマキ
昨日のわんどろの投稿イラストをRTします！！！(ﾟ∇^*)
{tweets.Length}作品の投稿をいただきました！

次回は {nextDate?.ToString(@"MM/dd\(ddd\)") ?? "未定"} の予定です、お楽しみに！！

▼イベントルール詳細
{HELP_URL}
                ";
            }
            else
            {
                text = $@"
{HASH_TAG}
昨日のわんどろの投稿イラストをRT……
って、誰も投稿してくれなかったみたい…(´；ω；｀)

次回は {nextDate?.ToString(@"MM/dd\(ddd\)") ?? "未定"} の予定です、よろしくおねがいします。

▼イベントルール詳細
{HELP_URL}
                ";
            }
            return text.Trim();
        }

        public static byte[] GenerateTextImage(string text)
        {
            using var image = Image.Load("docs/img/maki_theme_template.png");
            image.Mutate(context =>
            {
                var font = new FontCollection().Install("font/Corporate-Logo-Rounded.ttf");
                var option = new DrawingOptions();
                option.TextOptions.VerticalAlignment = VerticalAlignment.Center;
                option.TextOptions.HorizontalAlignment = HorizontalAlignment.Left;
                option.TextOptions.FallbackFonts.Add(new FontCollection().Install("font/TwemojiMozilla.ttf")); // 幅計算がうまく行ってないっぽい
                context.DrawText(
                    options: option,
                    text: text,
                    font: font.CreateFont(160, FontStyle.Bold),
                    color: Color.Black,
                    location: new PointF(image.Width * 0.375f, image.Height * 0.45f));
            });
            using var buffer = new MemoryStream();
            image.SaveAsPng(buffer);
            return buffer.ToArray();
        }

        public static string Dashboard(Recentry[] recently, Post[] postRanking, Post[] entryRanking, Post[] continueRanking)
        {
            var medias = Enumerable.Range(0, 5)
                .Select(i => LinkedMedia(
                    screenName: recently.ElementAtOrDefault(i)?.User?.ScreenName,
                    statusId: recently.ElementAtOrDefault(i)?.Post?["id_status"],
                    mediaUrl: recently.ElementAtOrDefault(i)?.Post?["url_media"]));

            var text = @$"
[📝基本ルール](#基本ルール)

## ランキング

### 最近の作品

| 1️⃣ | 2️⃣ | 3️⃣ | 4️⃣ | 5️⃣ | 6️⃣ | 7️⃣ | 8️⃣ | 9️⃣ | 🔟 |
| :---: | :---: | :---: | :---: | :---: | :---: | :---: | :---: | :---: | :---: |
| {string.Join(" | ", medias)} |
| {string.Join(" | ", Enumerable.Range(0, 10).Select(i => LinkedName(recently.ElementAtOrDefault(i)?.User)))} |


[全てみる]({HELP_URL_RECENTRY})


### 🏆Best 作品数🏆

沢山のマキマキイラスト作品を描き上げた方々です！

| 🥇 | 🥈 | 🥉 |
| :---: | :---: | :---: |
| {string.Join(" | ", Enumerable.Range(0, 3).Select(i => LinkedImage(postRanking.ElementAtOrDefault(i)?.User)))} |
| {string.Join(" | ", Enumerable.Range(0, 3).Select(i => LinkedName(postRanking.ElementAtOrDefault(i)?.User)))} |
| {string.Join(" | ", Enumerable.Range(0, 3).Select(i => $"{postRanking.ElementAtOrDefault(i)?.Count} 作品"))} |

[全てみる]({HELP_URL_POST_RANK})

### 🏆Best 参加回数🏆

イベントに沢山参加してくださった方々です！

| 🥇 | 🥈 | 🥉 |
| :---: | :---: | :---: |
| {string.Join(" | ", Enumerable.Range(0, 3).Select(i => LinkedImage(entryRanking.ElementAtOrDefault(i)?.User)))} |
| {string.Join(" | ", Enumerable.Range(0, 3).Select(i => LinkedName(entryRanking.ElementAtOrDefault(i)?.User)))} |
| {string.Join(" | ", Enumerable.Range(0, 3).Select(i => $"{entryRanking.ElementAtOrDefault(i)?.Count} 回"))} |

[全てみる]({HELP_URL_ENTRY_RANK})

### 🏆Best 継続数🏆

継続的に参加してくださっている方々です！

| 🥇 | 🥈 | 🥉 |
| :---: | :---: | :---: |
| {string.Join(" | ", Enumerable.Range(0, 3).Select(i => LinkedImage(continueRanking.ElementAtOrDefault(i)?.User)))} |
| {string.Join(" | ", Enumerable.Range(0, 3).Select(i => LinkedName(continueRanking.ElementAtOrDefault(i)?.User)))} |
| {string.Join(" | ", Enumerable.Range(0, 3).Select(i => $"{continueRanking.ElementAtOrDefault(i)?.Count} 回連続"))} |

[全てみる]({HELP_URL_CONTINUE_RANK})

{File.ReadAllText("README.md")}
            ";
            return text.Trim();
        }

        public static string RecentryPage(Recentry[] recently)
        {
            var text = @$"
[戻る]({HELP_URL_INDEX})

[📝基本ルール](#基本ルール)

## 全ての作品

| サムネイル | イベント日 | アイコン | ユーザー名 |
| :--: | :--: | :--: | :--: |
{string.Join("\n", recently.Select((post, i) =>
{
    var media = LinkedMedia(
        screenName: post?.User?.ScreenName,
        statusId: post?.Post?["id_status"],
        mediaUrl: post?.Post?["url_media"]);
    return $"| {media} | {post.Post["id_schedule"]} | {LinkedImage(post?.User)} | {LinkedName(post?.User)} |";
}))}

{File.ReadAllText("README.md")}
            ";
            return text.Trim();
        }

        public static string PostRankingPage(Post[] postRanking)
        {
            var text = @$"
[戻る]({HELP_URL_INDEX})

[📝基本ルール](#基本ルール)

## 🏆Best 作品数🏆 （全て）

| No | アイコン | ユーザー名 | スコア |
| :--: | :--: | :--: | :--: |
{string.Join("\n", postRanking.Select((post, i) =>
{
    return $"| {i + 1} | {LinkedImage(post?.User)} | {LinkedName(post?.User)} | {post?.Count} 作品 |";
}))}

{File.ReadAllText("README.md")}
            ";
            return text.Trim();
        }

        public static string EntryRankingPage(Post[] entryRanking)
        {
            var text = @$"
[戻る]({HELP_URL_INDEX})

[📝基本ルール](#基本ルール)

## 🏆Best 参加回数🏆 （全て）

| No | アイコン | ユーザー名 | スコア |
| :--: | :--: | :--: | :--: |
{string.Join("\n", entryRanking.Select((post, i) =>
{
    return $"| {i + 1} | {LinkedImage(post?.User)} | {LinkedName(post?.User)} | {post?.Count} 回 |";
}))}

{File.ReadAllText("README.md")}
            ";
            return text.Trim();
        }
    
        public static string ContinueRankingPage(Post[] continueRanking)
        {
            var text = @$"
[戻る]({HELP_URL_INDEX})

[📝基本ルール](#基本ルール)

## 🏆Best 継続数🏆 （全て）

| No | アイコン | ユーザー名 | スコア |
| :--: | :--: | :--: | :--: |
{string.Join("\n", continueRanking.Select((post, i) =>
{
    return $"| {i + 1} | {LinkedImage(post?.User)} | {LinkedName(post?.User)} | {post?.Count} 回連続 |";
}))}

{File.ReadAllText("README.md")}
            ";
            return text.Trim();
        }

        static string LinkedMedia(string screenName, string statusId, string mediaUrl) => $"[![]({mediaUrl}:thumb)](https://twitter.com/{screenName}/status/{statusId})";
        static string LinkedName(User user) => $"[@{user?.ScreenName}](https://twitter.com/{user?.ScreenName})";
        static string LinkedImage(User user) => $"[![@{user?.ScreenName}]({user?.ProfileImageUrlHttps.Replace("_normal.", "_bigger.")})](https://twitter.com/{user?.ScreenName})";

    }

    record Recentry(User User, Entry Post);
    record Post(string Id, User User, IEnumerable<Entry> Posts, int Count);
    
}
