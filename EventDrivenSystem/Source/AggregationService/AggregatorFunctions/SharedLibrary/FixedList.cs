using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AggregatorFunctions.SharedLibrary
{
    public class FixedList<T>
    {
        private readonly Queue<T> _queue;
        private readonly int _maxSize;

        public FixedList(int maxSize)
        {
            _queue = new Queue<T>();
            _maxSize = maxSize;
        }

        public void Add(T item)
        {
            _queue.Enqueue(item);

            if (_queue.Count > _maxSize)
            {
                _queue.Dequeue(); // Remove the oldest item
            }
        }

        public T Peek()
        {
            if (_queue.Count == 0)
            {
                throw new InvalidOperationException("Queue is empty.");
            }

            return _queue.Peek();
        }

        public T Dequeue()
        {
            if (_queue.Count == 0)
            {
                throw new InvalidOperationException("Queue is empty.");
            }

            return _queue.Dequeue();
        }

        public int Count => _queue.Count;

        public List<T> ToList()
        {
            return _queue.ToList();
        }

        public IEnumerable<T> Where(Func<T, bool> predicate)
        {
            return _queue.Where(predicate);
        }

        public IEnumerable<TResult> Select<TResult>(Func<T, TResult> selector)
        {
            return _queue.Select(selector);
        }

        // Add more LINQ methods as needed:
        // public IEnumerable<T> OrderBy(...) 
        // public IEnumerable<T> OrderByDescending(...)
        // ...
    }
}
