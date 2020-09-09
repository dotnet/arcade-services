import { Component, Input, OnChanges, SimpleChanges } from '@angular/core';
import { ApplicationInsightsService } from 'src/app/services/application-insights.service';
import { graphlib, render, layout } from 'dagre-d3';
import { select } from 'd3';

import { FlowGraph, FlowRef, FlowEdge } from 'src/maestro-client/models';
import { RepoNamePipe } from 'src/app/pipes/repo-name.pipe';

function getRepositoryShortName(repo?: string): string {
  return repo && RepoNamePipe.prototype.transform(repo) || "unknown repository";
}

function getNodeLabel(node:FlowRef): string {
  // Split the org and the repository on separate lines to make the nodes shorter
  return `${getRepositoryShortName(node.repository).split('/').join("<br>")}<br>`+
         `${node.branch}`;
}

function getNodeTitle(node:FlowRef): string {
  let official = node.officialBuildTime == 0 ? "No successful runs in the last 30 days" : `${node.officialBuildTime.toFixed(2)} min`;
  let pr = node.prBuildTime == 0 ? "No successful runs in the last 30 days" : `${node.prBuildTime.toFixed(2)} min`;
  let goal = node.goalTimeInMinutes == 0 ? "No goal time set" : `${node.goalTimeInMinutes} min`;

  return `Repository: ${getRepositoryShortName(node.repository)}\n` +
         `Branch: ${node.branch}\n` +
         `Official Build: ${official}\n` +
         `Dep Flow: ${pr}\n` +
         `Best Case Path Time: ${node.bestCasePathTime.toFixed(2)} min\n` +
         `Worst Case Path Time: ${node.worstCasePathTime.toFixed(2)} min\n` +
         `Goal Time: ${goal}`;
}

function getNodeDescription(node:FlowRef): string {
  if (node.onLongestBuildPath) {
    return "A node that is on the longest build path";
  }
  return "A node that is not on the longest build path";
}

function getEdgeDescription(edge:FlowEdge, graph:FlowGraph): string {
  let from = graph.flowRefs.find(x => x.id == edge.fromId);
  let fromRepo: string | undefined = undefined;
  let fromBranch: string | undefined = undefined;
  let fromId = edge.fromId;

  if (from) {
    fromRepo = getRepositoryShortName(from.repository);
    fromBranch = from.branch;
  }

  if (fromRepo && fromBranch) {
    fromId = `${fromRepo}@${fromBranch}`;
  }

  let to = graph.flowRefs.find(x => x.id == edge.toId);
  let toRepo: string | undefined = undefined;
  let toBranch: string | undefined = undefined;
  let toId = edge.toId;

  if (to) {
    toRepo = getRepositoryShortName(to.repository);
    toBranch = to.branch;
  }

  if (toRepo && toBranch) {
    toId = `${toRepo}@${toBranch}`;
  }

  let description = `An edge that connects ${fromId} to ${toId}`;
  if (edge.onLongestBuildPath) {
    description = `${description}\non the longest build path`;
  }
  return description;
}

function drawFlowGraph(graph: FlowGraph, includeArcade: boolean) {
  var g = new graphlib.Graph().setGraph({
    ranksep: 25,
    ranker: 'tight-tree'
  });

  if (graph)
  {
    var singletons: string[] = [];
    var arcadeMasterNode: string = "";
    var arcade3xNode: string = "";

    for ( var flowRef of graph.flowRefs ) {
      if (!flowRef.id) {
        continue;
      }
      let nodeProperties:any = { 
        labelType: "html",
        label: getNodeLabel(flowRef),
        title: getNodeTitle(flowRef),
        description: getNodeDescription(flowRef),
      };

      if (flowRef.onLongestBuildPath) {
        nodeProperties.shape = "ellipse";
      }

      // Add all the nodes to the singletons list
      singletons.push(flowRef.id)

      // Find the arcade nodes, if they exist
      var isArcade = flowRef.repository && flowRef.repository.endsWith("arcade");

      if (isArcade && flowRef.branch == "master") {
        arcadeMasterNode = flowRef.id;
      }
      else if (isArcade && flowRef.branch == "release/3.x") {
        arcade3xNode = flowRef.id
      }
      
      // If this node is not an arcade node, or if we are including 
      // arcade in the graph, add the node to the graph
      if (!isArcade || includeArcade) {
        g.setNode(flowRef.id, nodeProperties);
      }
    }

    for (var edge of graph.flowEdges) {
      if (!edge.fromId || !edge.toId) {
        continue;
      }
      
      let edgeProperties:any = { arrowheadClass: 'arrowhead',
                    description: getEdgeDescription(edge, graph)};

      if (edge.onLongestBuildPath) {
        edgeProperties.style = "stroke: #FD625E; stroke-width: 3px; stroke-dasharray: 5,5;";
        edgeProperties.arrowheadClass = 'longestPath';
      }

      // Remove nodes that have outgoing edges from the singletons list, as long as that edge 
      // is not to the arcade master node
      var index = singletons.indexOf(edge.fromId);
      if (index > -1 && edge.toId != arcadeMasterNode ) {
        singletons.splice(index, 1);
      }

      // Remove nodes that have incoming edges from the singletons list, as long as that edge
      // is not from the arcade master node
      index = singletons.indexOf(edge.toId);
      if (index > -1 && edge.fromId != arcadeMasterNode) {
        singletons.splice(index, 1);
      }

      // Draw all of the edges, exclusing the arcade edges if we are not including the arcade nodes
      if (includeArcade || (edge.toId != arcade3xNode && edge.fromId != arcade3xNode && edge.toId != arcadeMasterNode && edge.fromId != arcadeMasterNode)) {
        g.setEdge(edge.fromId.toString(), edge.toId.toString(), edgeProperties);
      }
    }

    var previousSingleton: string | undefined = undefined;

    // Add invisible edges between all of the singleton nodes so that they are all displayed
    // vertically on the side of the graph, giving the rest of the graph more space
    for (var singleton of singletons) {
      if (previousSingleton != undefined) {
        g.setEdge(previousSingleton, singleton, { style: "visibility: hidden;" , arrowhead: 'undirected', arrowheadClass: 'invisible'})
      }

      // If any of the nodes were on the longest build path with arcade on the graph,
      // change their shape back to a regular node, so they no longer appear as part of the path.
      if (!includeArcade) {
        g.node(singleton).shape = 'rect';
      }

      previousSingleton = singleton;
    }
  }

  // Round the node corners
  g.nodes().forEach(function(v) {
    var node = g.node(v);
    node.rx = node.ry = 5;
  });

  var render_graph = new render();

  select('svg.flowgraph').selectAll('*').remove();
  select('svg.flowgraph').attr("viewBox", "");

  var svg = select("svg.flowgraph"),
      inner = svg.append("g");

  render_graph(inner as any,g);

  inner.selectAll("g.node")
    .append("svg:title")
    .text(function(v:any) { return g.node(v).title });

  inner.selectAll("g.node")
    .append("svg:desc")
    .text(function(v:any) { return g.node(v).description });

  inner.selectAll("g.node")
    .append("svg:a");
  inner.selectAll("g.edgePath")
    .append("svg:desc")
    .text(function(v:any) { return g.edge(v).description });

  var bbox = (svg.node() as SVGGraphicsElement).getBBox();
  var height = bbox.height < 800 ? 810 : bbox.height+10;
  var width = bbox.width < 800 ? 810 :bbox.width+10;

  svg.attr("viewBox", `-5 -5 ${width} ${height}`);
}

@Component({
  selector: 'mc-channel-graph',
  templateUrl: './channel-graph.component.html',
  styleUrls: ['./channel-graph.component.scss']
})
export class ChannelGraphComponent implements OnChanges {

  @Input() public graph?: FlowGraph;
  @Input() public includeArcade?: boolean;

  constructor(private ai: ApplicationInsightsService) { }

  ngOnChanges(changes: SimpleChanges) {
    if(changes.includeArcade && (changes.includeArcade.previousValue != changes.includeArcade.currentValue))
    {
      this.ai.trackEvent({name: "featureEnabled"}, 
        {
          featureName: "includeArcade",
          featureState: changes.includeArcade.currentValue
        });
    }

    var includeArcade: boolean = this.includeArcade ? this.includeArcade : false;
    if (this.graph) {
      drawFlowGraph(this.graph, includeArcade);
    }
  }
}
