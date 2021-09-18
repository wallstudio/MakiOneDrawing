using System;
using System.Collections.Generic;

namespace MakiOneDrawingBot
{
    class DisposableList<T> : List<T>, IDisposable where T : IDisposable
    {
        public DisposableList() : this(Array.Empty<T>()) {}
        public DisposableList(IEnumerable<T> collection) : base(collection) {}
        public void Dispose()
        {
            foreach (var item in this) item.Dispose();
        }
    }

    class DisposableDictionary<TKey, T> : Dictionary<TKey, T>, IDisposable where T : IDisposable
    {
        public DisposableDictionary() : this(Array.Empty<KeyValuePair<TKey, T>>()) {}
        public DisposableDictionary(IEnumerable<KeyValuePair<TKey, T>> collection) : base(collection) {}
        public void Dispose()
        {
            foreach (var item in Values) item.Dispose();
        }
    }

}
