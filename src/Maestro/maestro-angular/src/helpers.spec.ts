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
    it("should return empty if given empty", () => {
      runTest([], []);
    });
  });
});
