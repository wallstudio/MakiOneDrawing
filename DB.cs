using System;
using System.Linq;
using System.Collections.Generic;
using Google.Apis.Sheets.v4;
using Google.Apis.Sheets.v4.Data;
using Google.Apis.Auth.OAuth2;
using System.Collections;

namespace MakiOneDrawingBot;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct)]
public class TableNameAttribute : Attribute
{
    public string Name { get; }
    public TableNameAttribute(string name) => Name = name;
}

public class DB : IDisposable
{
    readonly Dictionary<string, TableBase> m_Tables = new();
    readonly SheetsService m_Service;
    readonly string m_SpreadSheetId;

    public DB(string googleServiceAccountJwt, string spreadSheetId)
    {
        var scopedCredential = GoogleCredential.FromJson(googleServiceAccountJwt).CreateScoped(new[] { SheetsService.Scope.Spreadsheets });
        m_Service = new SheetsService(new SheetsService.Initializer() { HttpClientInitializer = scopedCredential, });
        m_SpreadSheetId = spreadSheetId;
    }

    public Table<T> GetTable<T>() where T : EntryBase, new()
    {
        var type = typeof(Table<T>);
        var explicitName = typeof(T).GetCustomAttributes(true).OfType<TableNameAttribute>().FirstOrDefault();
        var tableName = explicitName?.Name ?? type.Name;
        if(!m_Tables.TryGetValue(tableName, out var table))
        {
            var tableProperty = m_Service.Spreadsheets.Get(m_SpreadSheetId).Execute().Sheets
                .First(s => s.Properties.Title == tableName);
            var r = tableProperty.Properties.GridProperties.RowCount;
            var c = tableProperty.Properties.GridProperties.ColumnCount;
            var data = m_Service.Spreadsheets.Values.Get(m_SpreadSheetId, $"{tableName}!A1:{To26(c ?? 0)}{r + 1}").Execute();
            m_Tables[tableName] = table = (Table<T>)Activator.CreateInstance(type, data);
        }
        return (Table<T>)table;
    }

    public void Store()
    {
        foreach (var table in m_Tables.Values)
        {
            table.Store(m_Service, m_SpreadSheetId);
        }
    }
    
    void IDisposable.Dispose() => Store();

    static string To26(int i)
    {
        IList<char> str = new List<char>();
        do
        {
            str.Add((char)('A' + (i % 26)));
            i /= 26;
        }
        while(i > 0);
        return new string(str.Reverse().ToArray());
    }
}

public abstract class TableBase
{
    protected readonly ValueRange m_RawData;

    public TableBase(ValueRange rawData) => m_RawData = rawData;

    public virtual void Store(SheetsService service, string spreadSheetId)
    {
        var req = service.Spreadsheets.Values.Update(m_RawData, spreadSheetId, m_RawData.Range);
        req.ValueInputOption = SpreadsheetsResource.ValuesResource.UpdateRequest.ValueInputOptionEnum.RAW;
        req.Execute();
    }
}

public class Table<T> : TableBase, IEnumerable<T> where T : EntryBase, new()
{
    public T this[string id]
    {
        get => m_Rows.FirstOrDefault(r => r.Id == id);
        set
        {
            var i = m_Rows.FindIndex(r => r.Id == id);
            if(i < m_Rows.Count)
                m_Rows[i] = value;
            else
                m_Rows.Add(value);
        }
    }

    readonly List<T> m_Rows = new();

    public Table(ValueRange rawData) : base(rawData)
    {
        var columnLabels = m_RawData.Values.First().Select(o => o.ToString()).ToArray();
        for (int i = 1, il = m_RawData.Values.Count; i < il; i++)
        {
            var row = m_RawData.Values[i].Select(o => o.ToString()).ToArray();
            var entry = new T();
            entry.Deserialize(new (columnLabels
                .Select((lab, i) => (lab, row.ElementAtOrDefault(i)))
                .ToDictionary(kv => kv.lab, kv => kv.Item2)));
            m_Rows.Add(entry);
        }
    }

    public override void Store(SheetsService service, string spreadSheetId)
    {
        var columnLabels = m_RawData.Values.First().Select(o => o.ToString()).ToArray();
        for (int row = 1, rowLimit = m_Rows.Count; row < rowLimit; row++)
        {
            if (m_RawData.Values.Count > row)
            {
                var cellMap = m_Rows[row-1].Serialize();
                var cells = columnLabels.Select(l => cellMap[l]).ToArray();
                m_RawData.Values[row].Clear();
                foreach (var cell in cells)
                {
                    m_RawData.Values[row].Add(cell);
                }
            }
            else
            {
                m_RawData.Values.Add(m_Rows[row].Serialize().OfType<object>().ToList());
            }
        }
        base.Store(service, spreadSheetId);
    }

    public IEnumerator<T> GetEnumerator() => m_Rows.GetEnumerator(); 
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}


public abstract class EntryBase
{
    [AttributeUsage(AttributeTargets.Property)]
    public class ColumnAttribute : Attribute {}

    [Column] public virtual string Id { get; set; }

    public virtual Dictionary<string, string> Serialize()
    {
        var type = GetType();
        var props = type.GetProperties()
            .Where(p => p.GetCustomAttributes(true).Any(attr => attr is ColumnAttribute))
            .ToDictionary(p => p.Name, p => p.GetValue(this)?.ToString());
        return props;
    }

    public virtual void Deserialize(Dictionary<string, string> columns)
    {
        var type = GetType();
        var props = type.GetProperties();
        foreach (var (label, value) in columns)
        {
            var prop = props.First(p => p.Name == label);
            if(prop.PropertyType == typeof(string))
            {
                prop.SetValue(this, value);
            }
        }
    }
}

