import { Component, Input, AfterContentInit } from '@angular/core';
import { graphlib, render } from 'dagre-d3';
import { select } from 'd3';

import { FlowGraph, FlowRef, FlowEdge } from 'src/maestro-client/models';
import { RepoNamePipe } from 'src/app/pipes/repo-name.pipe';
import { analyzeAndValidateNgModules } from '@angular/compiler';

function getRepositoryShortName(repo:string): string {
  return RepoNamePipe.prototype.transform(repo) || "unknown repository";
}

function getNodeLabel(node:FlowRef): string {
  return `${getRepositoryShortName(node.repository)}\n`+
         `${node.branch}`;
}

function getNodeTitle(node:FlowRef): string {
  let official = node.officialBuildTime == 0 ? "No successful runs in the last 7 days" : `${node.officialBuildTime.toFixed(2)} min`;
  let pr = node.prBuildTime == 0 ? "No successful runs in the last 7 days" : `${node.prBuildTime.toFixed(2)} min`;
  let goal = node.goalTime == 0 ? "No goal time set" : `${node.goalTime} min`;

  return `Repository: ${getRepositoryShortName(node.repository)}\n` +
         `Branch: ${node.branch}\n` +
         `Official Build: ${official}\n` +
         `Dep Flow: ${pr}\n` +
         `Best Case Path Time: ${node.bestCaseTime.toFixed(2)} min\n` +
         `Worst Case Path Time: ${node.worstCaseTime.toFixed(2)} min\n` +
         `Goal Time: ${goal}`;
}

function getNodeDescription(node:FlowRef): string {
  if (node.onLongestBuildPath) {
    return "A node that is on the longest build path";
  }
  return "A node that is not on the longest build path";
}

function getEdgeDescription(edge:FlowEdge, graph:FlowGraph): string {
  let from = graph.nodes.find(x => x.id == edge.fromId);
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

  let to = graph.nodes.find(x => x.id == edge.toId);
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

@Component({
  selector: 'mc-channel-graph',
  templateUrl: './channel-graph.component.html',
  styleUrls: ['./channel-graph.component.scss']
})
export class ChannelGraphComponent implements AfterContentInit {

  @Input() public graph?: FlowGraph;

  constructor() { }

  ngAfterContentInit() {
    var g = new graphlib.Graph().setGraph({});

    if (this.graph)
    {
      for ( var flowRef of this.graph.nodes ) {
        let nodeProperties:any = { 
          label: getNodeLabel(flowRef),
          title: getNodeTitle(flowRef),
          description: getNodeDescription(flowRef),
        };

        if (flowRef.onLongestBuildPath) {
          nodeProperties.shape = "ellipse";
        }

        g.setNode(flowRef.id, nodeProperties);
      }
      
      for (var edge of this.graph.edges) {
        let edgeProperties:any = { arrowheadClass: 'arrowhead',
                      description: getEdgeDescription(edge, this.graph)};

        if (edge.onLongestBuildPath) {
          edgeProperties.style = "stroke: #FD625E; stroke-width: 3px; stroke-dasharray: 5,5;";
          edgeProperties.arrowheadClass = 'longestPath';
        }
        
        g.setEdge(edge.fromId.toString(), edge.toId.toString(), edgeProperties);

      }
    }

    var render_graph = new render();

    var svg = select("svg.flowgraph"),
        inner = svg.append("g");

    render_graph(inner,g);

    inner.selectAll("g.node")
      .append("svg:title")
      .text(function(v) { return g.node(v).title });

    inner.selectAll("g.node")
      .append("svg:desc")
      .text(function(v) { return g.node(v).description });

    inner.selectAll("g.node")
      .append("svg:a");
    inner.selectAll("g.edgePath")
      .append("svg:desc")
      .text(function(v) { return g.edge(v).description });
  
    var bbox = (svg.node() as SVGGraphicsElement).getBBox();

    svg.attr("viewBox", `0 0 ${bbox.width} ${bbox.height}`);
  }
}