function log(str: string) {
  // console.log(str);
}

export function topologicalSort<TNode, TKey>(nodes: TNode[], getChildren: (node: TNode) => TNode[], getKey: (node: TNode) => TKey): TNode[] {
  // compute collection of edges
  let edges: {from: TKey, to: TKey}[] = [];
  for (const node of nodes) {
      for (const child of getChildren(node)) {
        edges.push({
          from: getKey(node),
          to: getKey(child)
        });
        log(getKey(node) + "->" + getKey(child));
      }
  }

  function hasIncommingEdges(node: TNode) {
    return edges.some(e => getKey(node) == e.to);
  }

  const sorted: TNode[] = [];
  const toProcess: TNode[] = nodes.filter(node => !hasIncommingEdges(node));
  while (toProcess.length) {
    const n = toProcess.pop() as TNode; // can't be undefined
    log("processing: " + getKey(n))
    sorted.push(n);
    for (const edge of edges.filter(e => e.from == getKey(n))) {
      log("processing edge: " + edge.from + "->" + edge.to)
      edges = edges.filter(e => e !== edge);
      log("new edges: " + edges.map(e => e.from + "->" + e.to).join(", "))
      const m = nodes.find(n => edge.to == getKey(n)) as TNode;
      if (!hasIncommingEdges(m)) {
        toProcess.unshift(m);
      }
    }
  }
  if (edges.length) {
    throw new Error("Cannot sort graph, it has cycles.");
  }

  return sorted.reverse();
}
