using System;
using System.Collections.Generic;

namespace MakiOneDrawingBot.Entries;


[TableName("schedule3")]
public class Schedule : EntryBase
{
    [Column] public TimeOnly? PreTime { get; set; }
    [Column] public TimeOnly? BeginTime { get; set; }
    [Column] public TimeOnly? EndTime { get; set; }
    [Column] public TimeOnly? AccTime { get; set; }
    [Column] public string Theme1 { get; set; }
    [Column] public string Theme2 { get; set; }
    [Column] public long? PreId { get; set; }
    [Column] public long? BeginId { get; set; }
    [Column] public long? EndId { get; set; }
    [Column] public long? AccId { get; set; }

    public override void Deserialize(Dictionary<string, string> columns)
    {
        base.Deserialize(columns);
        PreTime = TimeOnly.TryParse(columns[nameof(PreTime)], out var preTime) ? preTime : null;
        BeginTime = TimeOnly.TryParse(columns[nameof(BeginTime)], out var beginTime) ? beginTime : null;
        EndTime = TimeOnly.TryParse(columns[nameof(EndTime)], out var endTime) ? endTime : null;
        AccTime = TimeOnly.TryParse(columns[nameof(AccTime)], out var accTime) ? accTime : null;
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
