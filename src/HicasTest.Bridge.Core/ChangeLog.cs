using System.Collections.Generic;
using System.Linq;

namespace HicasTest.Bridge.Core
{
    /// <summary>
    /// Collects element ids reported by host change events and normalizes them:
    /// created-then-deleted elements disappear, and added elements are not also reported as modified.
    /// </summary>
    public sealed class ChangeLog
    {
        private readonly HashSet<string> _added = new HashSet<string>();
        private readonly HashSet<string> _modified = new HashSet<string>();
        private readonly HashSet<string> _deleted = new HashSet<string>();

        public void Clear()
        {
            _added.Clear();
            _modified.Clear();
            _deleted.Clear();
        }

        public void Added(string id)
        {
            _deleted.Remove(id);
            _added.Add(id);
        }

        public void Modified(string id)
        {
            if (!_added.Contains(id))
                _modified.Add(id);
        }

        public void Deleted(string id)
        {
            _modified.Remove(id);
            if (!_added.Remove(id))
                _deleted.Add(id);
        }

        public IReadOnlyList<string> AddedIds => _added.OrderBy(x => x).ToList();

        public IReadOnlyList<string> ModifiedIds => _modified.OrderBy(x => x).ToList();

        public IReadOnlyList<string> DeletedIds => _deleted.OrderBy(x => x).ToList();
    }
}
