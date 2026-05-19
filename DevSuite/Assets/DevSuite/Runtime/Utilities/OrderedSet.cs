using System.Collections;
using System.Collections.Generic;

namespace Ff.DevSuite
{
    internal class OrderedSet<T> : ICollection<T>
    {
        private readonly IDictionary<T, LinkedListNode<T>> _nodeByItem;
        private readonly LinkedList<T> _nodes;

        public OrderedSet() : this(null, EqualityComparer<T>.Default) { }

        public OrderedSet(IEnumerable<T> items) : this(items, EqualityComparer<T>.Default) { }

        public OrderedSet(IEnumerable<T> items, IEqualityComparer<T> comparer)
        {
            _nodeByItem = new Dictionary<T, LinkedListNode<T>>(comparer);
            _nodes = new LinkedList<T>();

            if (items != null)
            {
                foreach (var item in items)
                {
                    Add(item);
                }
            }
        }

        public int Count => _nodeByItem.Count;

        public virtual bool IsReadOnly => _nodeByItem.IsReadOnly;

        void ICollection<T>.Add(T item)
        {
            Add(item);
        }

        public bool Add(T item)
        {
            if (_nodeByItem.ContainsKey(item))
                return false;

            var node = _nodes.AddLast(item);
            _nodeByItem.Add(item, node);
            return true;
        }

        public void Clear()
        {
            _nodes.Clear();
            _nodeByItem.Clear();
        }

        public bool Remove(T item)
        {
            var found = _nodeByItem.TryGetValue(item, out var node);
            if (!found)
                return false;

            _nodeByItem.Remove(item);
            _nodes.Remove(node);
            return true;
        }

        public IEnumerator<T> GetEnumerator()
        {
            return _nodes.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        public bool Contains(T item)
        {
            return _nodeByItem.ContainsKey(item);
        }

        public void CopyTo(T[] array, int arrayIndex)
        {
            _nodes.CopyTo(array, arrayIndex);
        }
    }
}