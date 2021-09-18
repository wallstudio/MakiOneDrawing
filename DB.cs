using System;
using System.Collections;
using System.Linq;
using System.Collections.Generic;
using Google.Apis.Sheets.v4;
using Google.Apis.Sheets.v4.Data;
using Google.Apis.Auth.OAuth2;

namespace MakiOneDrawingBot
{
    class DB : DisposableDictionary<string, Table>
    {
        DB(IEnumerable<KeyValuePair<string, Table>> collection) : base(collection) {}

        public static DB Get(string googleServiceAccountJwt, string dbSpreadSheetId)
        {
            var service = new SheetsService(new SheetsService.Initializer()
            {
                HttpClientInitializer = GoogleCredential.FromJson(googleServiceAccountJwt).CreateScoped(new[]{ SheetsService.Scope.Spreadsheets }),
            });
            var tables = service.Spreadsheets.Get(dbSpreadSheetId).Execute().Sheets.Select(s => new Table(service.Spreadsheets, dbSpreadSheetId, s)).ToArray();
            return new DB(tables.ToDictionary(t => t.Name));
        }
    }

    class Table : IDisposable, IEnumerable<Entry>
    {
        public string Name => sheet.Properties.Title;
        public int? Id => sheet.Properties.SheetId;
        public Entry this[string id] => this.First(e => e["id"] == id);
        public string this[int row, int column]
        {
            get => data.Values.ElementAtOrDefault(row).ElementAtOrDefault(column) as string;
            set
            {
                while(row >= data.Values.Count) data.Values.Add(new List<object>());
                while(column >= data.Values[row].Count) data.Values[row].Add(null);
                data.Range = $"{Name}!A1:{To26(Size.column)}{Size.row + 1}";
                data.Values[row][column] = value;
                isDirty = true;
            }
        }
        public (int row, int column) Size => (data.Values.Count, data.Values.Max(r => r.Count));

        readonly SpreadsheetsResource resource;
        readonly string spreadsheetId;
        readonly Sheet sheet;
        readonly ValueRange data;
        public string[] Keys;
        bool isDirty = false;

        public Table(SpreadsheetsResource resource, string spreadsheetId, Sheet sheet)
        {
            this.resource = resource;
            this.spreadsheetId = spreadsheetId;
            this.sheet = sheet;

            var r = sheet.Properties.GridProperties.RowCount;
            var c = sheet.Properties.GridProperties.ColumnCount;
            data = resource.Values.Get(spreadsheetId, $"{Name}!A1:{To26(c ?? 0)}{r + 1}").Execute();

            Keys = Enumerable.Range(0, Size.column).Select(i => this[0, i]).ToArray();
        }
        
        public bool TryGet(string id, out Entry entry) => null != (entry = this.FirstOrDefault(e => e["id"] == id));

        public void Dispose()
        {
            if(isDirty)
            {
                var req = resource.Values.Update(data, spreadsheetId, data.Range);
                req.ValueInputOption = SpreadsheetsResource.ValuesResource.UpdateRequest.ValueInputOptionEnum.RAW;
                req.Execute();
            }
        }

        public IEnumerator<Entry> GetEnumerator()
        {
            for (int i = 1, l = Size.row; i < l; i++)
            {
                yield return new Entry(this, i);
            }
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

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

        public Entry Add(string id)
        {
            if(this.Any(e => e[nameof(id)] == id)) throw new ArgumentException($"dup entry ({id})");

            this[Size.row, Array.IndexOf(Keys, nameof(id))] = id;
            return this.First(e => e[nameof(id)] == id);
        }
    }

    class Entry : IEnumerable<KeyValuePair<string, string>>
    {
        readonly Table table;
        readonly int rowIndex;
        public string this[string key]
        {
            get => table[rowIndex, Array.IndexOf(table.Keys, key)];
            set => table[rowIndex, Array.IndexOf(table.Keys, key)] = value;
        }

        public Entry(Table table, int rowIndex)
        {
            this.table = table;
            this.rowIndex = rowIndex;
        }

        public IEnumerator<KeyValuePair<string, string>> GetEnumerator()
        {
           foreach (var key in table.Keys) yield return new KeyValuePair<string, string>(key, this[key]);
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public override string ToString() => string.Join(",", this.Select(kv => kv.Value));
    }

}
