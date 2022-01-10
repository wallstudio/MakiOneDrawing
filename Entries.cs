using System.Collections.Generic;

namespace MakiOneDrawingBot.Entries;


[TableName("schedule2")]
public class Schedule : EntryBase
{
    [Column] public string Theme1 { get; set; }
    [Column] public string Theme2 { get; set; }
    [Column] public long? PreId { get; set; }
    [Column] public long? BeginId { get; set; }
    [Column] public long? EndId { get; set; }
    [Column] public long? AccId { get; set; }

    public override void Deserialize(Dictionary<string, string> columns)
    {
        base.Deserialize(columns);
        PreId = long.TryParse(columns[nameof(PreId)], out var preId) ? preId : null;
        BeginId = long.TryParse(columns[nameof(BeginId)], out var beginId) ? beginId : null;
        EndId = long.TryParse(columns[nameof(EndId)], out var endId) ? endId : null;
        AccId = long.TryParse(columns[nameof(AccId)], out var accId) ? accId : null;
    }
}

[TableName("post2")]
public class Post : EntryBase
{
    [Column] public string ScheduleId { get; set; }
    [Column] public string UserName { get; set; }
}
