using System.Diagnostics;

namespace ProMapCargo.OfflineBundleBuilder;

/// <summary>
/// Contraction Hierarchies preprocessing. Nodes are contracted in order of a lazy
/// priority (edge difference + contracted neighbours); for every pair of neighbours
/// u→v→w a shortcut u→w is added unless a witness path without v is at most as short.
/// The result is the node rank and the final adjacency, from which the upward search
/// graphs of the bundle are built.
/// </summary>
public sealed class Contractor
{
    public struct Edge
    {
        public int Node;
        public int Weight;
        public int Arc;
    }

    private const int WitnessSettleLimit = 400;
    private const int SimulationSettleLimit = 60;

    private readonly int nodeCount;
    private readonly List<Edge>[] outEdges;
    private readonly List<Edge>[] inEdges;
    private readonly bool[] contracted;
    private readonly int[] deletedNeighbours;

    // Arcs: original ones first, shortcuts appended.
    public List<int> ArcA { get; } = [];
    public List<int> ArcB { get; } = [];

    public int[] Rank { get; }

    // Witness search scratch space (timestamped to avoid clearing).
    private readonly int[] witnessDistance;
    private readonly int[] witnessStamp;
    private int stamp;

    public Contractor(int nodeCount)
    {
        this.nodeCount = nodeCount;
        outEdges = new List<Edge>[nodeCount];
        inEdges = new List<Edge>[nodeCount];
        for (var i = 0; i < nodeCount; i++)
        {
            outEdges[i] = new List<Edge>(3);
            inEdges[i] = new List<Edge>(3);
        }

        contracted = new bool[nodeCount];
        deletedNeighbours = new int[nodeCount];
        Rank = new int[nodeCount];
        witnessDistance = new int[nodeCount];
        witnessStamp = new int[nodeCount];
    }

    /// <summary>Adds an original arc (keeps only the cheapest between two nodes).</summary>
    public void AddOriginalArc(int from, int to, int weight, int roadCode)
    {
        if (from == to)
        {
            return;
        }

        var list = outEdges[from];
        for (var i = 0; i < list.Count; i++)
        {
            if (list[i].Node == to)
            {
                if (weight < list[i].Weight)
                {
                    var arc = list[i].Arc;
                    ArcA[arc] = -roadCode - 1;
                    SetWeight(from, to, weight, arc);
                }

                return;
            }
        }

        var id = ArcA.Count;
        ArcA.Add(-roadCode - 1);
        ArcB.Add(-1);
        outEdges[from].Add(new Edge { Node = to, Weight = weight, Arc = id });
        inEdges[to].Add(new Edge { Node = from, Weight = weight, Arc = id });
    }

    public void Run()
    {
        var watch = Stopwatch.StartNew();
        var queue = new PriorityQueue<int, long>(nodeCount);
        var priority = new long[nodeCount];
        for (var v = 0; v < nodeCount; v++)
        {
            priority[v] = Priority(v);
            queue.Enqueue(v, priority[v]);
        }

        Console.WriteLine($"  initial priorities: {watch.Elapsed.TotalSeconds:0.0} s");

        var order = 0;
        var shortcuts = 0;
        while (queue.TryDequeue(out var v, out var queued))
        {
            if (contracted[v] || queued != priority[v])
            {
                continue;
            }

            // Lazy update: re-evaluate; if no longer the minimum, put it back.
            var current = Priority(v);
            if (queue.TryPeek(out _, out var next) && current > next)
            {
                priority[v] = current;
                queue.Enqueue(v, current);
                continue;
            }

            shortcuts += Contract(v, simulate: false);
            contracted[v] = true;
            Rank[v] = order++;

            foreach (var neighbour in Neighbours(v))
            {
                deletedNeighbours[neighbour]++;
                var updated = Priority(neighbour);
                if (updated != priority[neighbour])
                {
                    priority[neighbour] = updated;
                    queue.Enqueue(neighbour, updated);
                }
            }

            if (order % 50_000 == 0)
            {
                Console.WriteLine($"  contracted {order:N0}/{nodeCount:N0} · shortcuts {shortcuts:N0} · {watch.Elapsed.TotalSeconds:0} s");
            }
        }

        Console.WriteLine($"  done: {shortcuts:N0} shortcuts in {watch.Elapsed.TotalSeconds:0} s");
    }

    /// <summary>Final adjacency: (from, to, weight, arc) for every arc still in the graph.</summary>
    public IEnumerable<(int From, int To, int Weight, int Arc)> FinalArcs()
    {
        for (var u = 0; u < nodeCount; u++)
        {
            foreach (var edge in outEdges[u])
            {
                yield return (u, edge.Node, edge.Weight, edge.Arc);
            }
        }
    }

    private IEnumerable<int> Neighbours(int v)
    {
        var seen = new HashSet<int>();
        foreach (var edge in outEdges[v])
        {
            if (!contracted[edge.Node] && seen.Add(edge.Node))
            {
                yield return edge.Node;
            }
        }

        foreach (var edge in inEdges[v])
        {
            if (!contracted[edge.Node] && seen.Add(edge.Node))
            {
                yield return edge.Node;
            }
        }
    }

    private long Priority(int v)
    {
        var added = Contract(v, simulate: true);
        var removed = 0;
        foreach (var edge in outEdges[v])
        {
            if (!contracted[edge.Node]) removed++;
        }

        foreach (var edge in inEdges[v])
        {
            if (!contracted[edge.Node]) removed++;
        }

        return 2L * (added - removed) + deletedNeighbours[v];
    }

    /// <summary>Adds (or counts, when simulating) the shortcuts needed to remove v.</summary>
    private int Contract(int v, bool simulate)
    {
        var count = 0;
        var outgoing = outEdges[v];
        var settleLimit = simulate ? SimulationSettleLimit : WitnessSettleLimit;

        foreach (var incoming in inEdges[v])
        {
            var u = incoming.Node;
            if (contracted[u])
            {
                continue;
            }

            var maxCost = 0;
            foreach (var o in outgoing)
            {
                if (!contracted[o.Node] && o.Node != u)
                {
                    maxCost = Math.Max(maxCost, incoming.Weight + o.Weight);
                }
            }

            if (maxCost == 0)
            {
                continue;
            }

            WitnessSearch(u, v, maxCost, settleLimit);

            foreach (var o in outgoing)
            {
                var w = o.Node;
                if (contracted[w] || w == u)
                {
                    continue;
                }

                var viaCost = incoming.Weight + o.Weight;
                if (witnessStamp[w] == stamp && witnessDistance[w] <= viaCost)
                {
                    continue;
                }

                count++;
                if (!simulate)
                {
                    AddShortcut(u, w, viaCost, incoming.Arc, o.Arc);
                }
            }
        }

        return count;
    }

    private void WitnessSearch(int source, int excluded, int maxCost, int settleLimit)
    {
        stamp++;
        var queue = new PriorityQueue<int, int>();
        witnessStamp[source] = stamp;
        witnessDistance[source] = 0;
        queue.Enqueue(source, 0);
        var settled = 0;

        while (queue.TryDequeue(out var node, out var distance))
        {
            if (distance > witnessDistance[node])
            {
                continue;
            }

            if (distance > maxCost || ++settled > settleLimit)
            {
                break;
            }

            foreach (var edge in outEdges[node])
            {
                var next = edge.Node;
                if (next == excluded || contracted[next])
                {
                    continue;
                }

                var candidate = distance + edge.Weight;
                if (witnessStamp[next] != stamp || candidate < witnessDistance[next])
                {
                    witnessStamp[next] = stamp;
                    witnessDistance[next] = candidate;
                    queue.Enqueue(next, candidate);
                }
            }
        }
    }

    private void AddShortcut(int from, int to, int weight, int arcA, int arcB)
    {
        var list = outEdges[from];
        for (var i = 0; i < list.Count; i++)
        {
            if (list[i].Node == to)
            {
                if (weight < list[i].Weight)
                {
                    var id = NewArc(arcA, arcB);
                    SetWeight(from, to, weight, id);
                }

                return;
            }
        }

        var arc = NewArc(arcA, arcB);
        outEdges[from].Add(new Edge { Node = to, Weight = weight, Arc = arc });
        inEdges[to].Add(new Edge { Node = from, Weight = weight, Arc = arc });
    }

    private int NewArc(int a, int b)
    {
        ArcA.Add(a);
        ArcB.Add(b);
        return ArcA.Count - 1;
    }

    private void SetWeight(int from, int to, int weight, int arc)
    {
        var outList = outEdges[from];
        for (var i = 0; i < outList.Count; i++)
        {
            if (outList[i].Node == to)
            {
                outList[i] = new Edge { Node = to, Weight = weight, Arc = arc };
            }
        }

        var inList = inEdges[to];
        for (var i = 0; i < inList.Count; i++)
        {
            if (inList[i].Node == from)
            {
                inList[i] = new Edge { Node = from, Weight = weight, Arc = arc };
            }
        }
    }
}
