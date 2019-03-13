import { topologicalSort } from "./helpers";

interface Node {
  id: number;
  children: number[];
}

function getKey(node: Node): number {
  return node.id;
}

function getChildrenFn(nodes: Node[]): (node: Node) => Node[] {
  return function (node: Node) {
    return node.children.map(id => nodes.find(n => n.id === id)!);
  };
}


describe("helpers", () => {

  describe("topologicalSort", () => {
    function runTest(nodes: Node[], expectedList: Node[]) {
      const result = topologicalSort(nodes, getChildrenFn(nodes), getKey);
      expect(result).toEqual(expectedList);
    }

    let nodeId: number;
    beforeEach(() => {
      nodeId = 1;
    });

    function node(...children: Node[]) {
      return {
        id: nodeId++,
        children: children.map(c => c.id),
      };
    }

    it("should return empty if given empty", () => {
      runTest([], []);
    });

    it("should return a single node given a single node", () => {
      const a = node();
      runTest([a], [a]);
    });

    it("should sort 2 nodes properly", () => {
      const a = node();
      const b = node(a);
      runTest([b, a], [a, b]);
    });

    it("should sort many nodes properly", () => {
      const a = node();
      const b = node(a);
      const c = node(b, a);
      const d = node(a, b, c);
      const e = node(a, d);
      const f = node(a, e);
      runTest([f, e, b, d, a, c], [a, b, c, d, e, f]);
    });
  });
});
