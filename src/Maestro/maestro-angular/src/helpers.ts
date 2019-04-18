import { Build } from 'src/maestro-client/models';
import { startOfMinute, addMinutes, startOfHour, addHours, differenceInMilliseconds, startOfDay, addDays } from 'date-fns';
import { concat, of, timer } from 'rxjs';
import { tap } from 'rxjs/operators';

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
    toProcess.sort((a, b) => {
      const ak = getKey(a);
      const bk = getKey(b);
      if (ak < bk) {
        return -1;
      }
      if (ak > bk) {
        return 1;
      }
      return 0;
    })
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


export function onThe(time: "minute" | "hour" | "day") {
  let start: ((d: Date) => Date) | undefined = undefined;
  let add: ((d: Date, n: number) => Date) | undefined = undefined;
  let interval: number | undefined = undefined;
  if (time === "minute") {
    start = startOfMinute;
    add = addMinutes;
    interval = 1000 * 60;
  } else if (time === "hour") {
    start = startOfHour;
    add = addHours;
    interval = 1000 * 60 * 60;
  } else if (time === "day") {
    start = startOfDay;
    add = addDays;
    interval = 1000 * 60 * 60 * 24;
  }
  if (!start || !add || !interval) {
    throw new Error(`time '${time}' is invalid`);
  }

  const delay = differenceInMilliseconds(start(add(new Date(), 1)), new Date());
  return concat(of(0), timer(delay, interval));
}

export function tapLog<T>(message: string) {
  return tap<T>(v => console.log(message, v));
}
