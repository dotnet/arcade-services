import { Component, Input, OnChanges, SimpleChanges, AfterContentInit } from '@angular/core';
import { graphlib, render } from 'dagre-d3';
import { select } from 'd3';

import { FlowGraph, FlowRef, FlowEdge } from 'src/maestro-client/models';
import { RepoNamePipe } from 'src/app/pipes/repo-name.pipe';
import { analyzeAndValidateNgModules } from '@angular/compiler';

function getRepositoryShortName(repo:string): string {
  return `${RepoNamePipe.prototype.transform(repo)}`;
}
function getNodeLabel(node:FlowRef): string {
  let label = `${getRepositoryShortName(node.repository)}\n`;
  label = `${label}${node.branch}\n`;
  return label;
}

function getNodeTitle(node:FlowRef): string {
  let official = node.officialBuildTime == 0 ? "No successful runs in the last 7 days" : node.officialBuildTime.toFixed(2);
  let pr = node.prBuildTime == 0 ? "No successful runs in the last 7 days" : node.prBuildTime.toFixed(2);

  let title = `Repository: ${getRepositoryShortName(node.repository)}\n`;
  title = `${title}Branch: ${node.branch}\n`;
  title = `${title}Official Build: ${official}\n`;
  title = `${title}Dep Flow: ${pr}\n`;
  title = `${title}Best Case Path Time: ${node.bestCaseTime.toFixed(2)}\n`;
  title = `${title}Worst Case Path Time: ${node.worstCaseTime.toFixed(2)}`;
  return title;
}

function getNodeDescription(node:FlowRef): string {
  let description = "A node that is not on the longest build path";
  if (node.onLongestBuildPath) {
    description = "A node that is on the longest build path";
  }
  return description;
}

function getEdgeDescription(edge:FlowEdge, graph:FlowGraph): string {
  let from = graph.nodes.find(x => x.id == edge.fromId);
  let fromRepo = "";
  let fromBranch = "";
  if (from) {
    fromRepo = getRepositoryShortName(from.repository);
    fromBranch = from.branch;
  }

  let to = graph.nodes.find(x => x.id == edge.toId);
  let toRepo = "";
  let toBranch = "";

  if (to) {
    toRepo = getRepositoryShortName(to.repository);
    toBranch = to.branch;
  }

  let description = `An edge that connects ${fromRepo}@${fromBranch} to ${toRepo}@${toBranch}`;
  if (edge.onLongestBuildPath) {
    description = `${description}\nOn the longest build path`;
  }
  return description;
}

@Component({
  selector: 'mc-channel-graph',
  templateUrl: './channel-graph.component.html',
  styleUrls: ['./channel-graph.component.scss']
})
export class ChannelGraphComponent implements OnChanges, AfterContentInit {

  @Input() public graph?: FlowGraph;

  constructor() { }

  ngOnChanges(changes: SimpleChanges) {
  }

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