namespace Nethermind.Trie
{
    public readonly struct NodeCommitInfo
    {
        public NodeCommitInfo()
        {
            ChildPositionAtParent = 0;
            NodeParent = null;
        }

        public NodeCommitInfo(
            TrieNode nodeParent,
            int childPositionAtParent)
        {
            ChildPositionAtParent = childPositionAtParent;
            NodeParent = nodeParent;
        }

        public TrieNode? NodeParent { get; }

        public int ChildPositionAtParent { get; }

        public bool IsEmptyBlockMarker { get; }

        public bool IsRoot => !IsEmptyBlockMarker && NodeParent is null;

        public override string ToString()
        {
            return $"[{nameof(NodeCommitInfo)}|{(NodeParent is null ? "root" : $"child {ChildPositionAtParent} of {NodeParent}")}]";
        }
    }
}
