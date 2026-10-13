#nullable enable

using System.Collections.Generic;

namespace UVWizard.Core.Islands
{
    /// <summary>Disjoint sets over 0..count-1, with path halving.</summary>
    internal sealed class UnionFind
    {
        readonly int[] _parent;

        public UnionFind(int count)
        {
            _parent = new int[count];
            for (var i = 0; i < count; i++) _parent[i] = i;
        }

        public int Find(int i)
        {
            while (_parent[i] != i)
            {
                _parent[i] = _parent[_parent[i]];
                i = _parent[i];
            }
            return i;
        }

        public void Union(int a, int b)
        {
            _parent[Find(a)] = Find(b);
        }

        /// <summary>The sets, each in ascending order, in order of their smallest member.</summary>
        public List<List<int>> Groups()
        {
            var groupOfRoot = new Dictionary<int, List<int>>();
            var groups = new List<List<int>>();
            for (var i = 0; i < _parent.Length; i++)
            {
                var root = Find(i);
                if (!groupOfRoot.TryGetValue(root, out var members))
                {
                    members = new List<int>();
                    groupOfRoot.Add(root, members);
                    groups.Add(members);
                }
                members.Add(i);
            }
            return groups;
        }
    }
}
