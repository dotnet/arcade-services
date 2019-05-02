import { Component, Input, OnChanges, SimpleChanges } from '@angular/core';
import { Build, BuildGraph, BuildRef } from 'src/maestro-client/models';
import { topologicalSort } from 'src/helpers';
import { trigger, transition, style, animate } from '@angular/animations';

type BuildState = "locked" | "unlocked" | "conflict" | "ancestor" | "parent" | "child";

interface BuildData {
  build: Build;
  coherent: {
    withAll: boolean;
    withProduct: boolean;
  };
  isDependent?: boolean;
  isParent?: boolean;
  isAncestor?: boolean;
  isSameRepository?: boolean;
  isFocused?: boolean;
  isLocked?: boolean;
  isToolset?: boolean;
  isRootOrImmediateDependency?: boolean;
  state?: BuildState;
  hasIncoherentDependencies?: boolean;
  hasIncoherentDependenciesIncludingToolsets?: boolean;
}

function getState(b: BuildData): BuildState | undefined {
  if (b.isFocused) {
    if (b.isLocked) {
      return "locked";
    } else {
      return "unlocked";
    }
  }

  if (b.isSameRepository) {
    return "conflict";
  }

  if (b.isAncestor) {
    return "ancestor";
  }

  if (b.isParent) {
    return "parent";
  }

  if(b.isDependent) {
    return "child";
  }
}

function getRepo(build: Build) {
  return build.gitHubRepository || build.azureDevOpsRepository;
}

function isToolset(build: Build, graph: BuildGraph) {
  // A build is a "toolset" if it has at least one dependency pointing to it,
  // and all dependencies pointing to it are toolset dependencies
  let hasParents = false;
  for (const b of Object.values(graph.builds)) {
    if (b.dependencies) {
      for (const dep of b.dependencies) {
        if (dep.buildId == build.id) {
          hasParents = true;
          if (dep.isProduct) {
            return false;
          }
        }
      }
    }
  }
  return hasParents;
}

function sortBuilds(graph: BuildGraph): BuildData[] {
  const sortedBuilds = topologicalSort(Object.values(graph.builds), build => {
    if (build.dependencies) {
      return build.dependencies.map(dep => graph.builds[dep.buildId]);
    }
    return [];
  }, build => build.id);  

  let result = sortedBuilds.map<BuildData>(build => {
      const sameRepo = sortedBuilds.filter(b => getRepo(b) === getRepo(build));
      const sameRepoProducts = sameRepo.filter(b => !isToolset(b, graph));
      const coherentWithAll = sameRepo.every(b => b.id <= build.id);
      const coherentWithProducts = sameRepoProducts.every(b => b.id <= build.id);

      return {
        build: build,
        coherent: {
          withAll: coherentWithAll,
          withProduct: coherentWithProducts,
        },
      };
  });

  // We want to check the coherency of dependencies in this order, as BuildData array will start from the lowest dependency first. This will
  // eliminate the need for recursion in the hasIncoherentDependencies method.
  for (const node of result) {
    node.hasIncoherentDependencies = hasIncoherentDependencies(node.build, result, false);
    node.hasIncoherentDependenciesIncludingToolsets = hasIncoherentDependencies(node.build, result, true);
  }

  result = result.reverse();

  if(result[0].build.dependencies)
  {
    result[0].isRootOrImmediateDependency = true;
    for (const dependecy of result[0].build.dependencies)
    {
      if(dependecy)
      {
        let dep = result.find(d => d.build.id == dependecy.buildId);
        if(dep)
        {
          dep.isRootOrImmediateDependency = true;
        }
      }
    }
  }

  for (const node of result) {
    node.isToolset = isToolset(node.build, graph);
  }

  return result;
}

// Given a build, determines if that build has incoherent dependencies at any level. Searches through toolset incoherencies if necessary. 
function hasIncoherentDependencies(build: Build, buildData: BuildData[], includeToolsets: boolean): boolean {
  let currentBuildData = buildData.find(x => x.build.id == build.id);
  if(currentBuildData)
  {
    if(buildHasIncoherentDependencies(currentBuildData, includeToolsets))
    {
      return true;
    }

    if (currentBuildData.build.dependencies) {
      for (const dep of currentBuildData.build.dependencies) {
        if(shouldConsiderDependecy(dep, includeToolsets)){ 
          let depBuildData = buildData.find(r => r.build.id == dep.buildId);
          if(depBuildData && (dependencyIsIncoherent(depBuildData, includeToolsets) || buildHasIncoherentDependencies(depBuildData, includeToolsets)))
          {
            return true;
          }
        }
      }
    }
  }

  return false;
}

function shouldConsiderDependecy(dependecy: BuildRef, includeToolsets: boolean): boolean {
  return !(!includeToolsets && !dependecy.isProduct);
}

function dependencyIsIncoherent(buildData: BuildData, includeToolsets: boolean): boolean {
  return includeToolsets ? !buildData.coherent.withAll : !buildData.coherent.withProduct;
}

function buildHasIncoherentDependencies(buildData: BuildData, includeToolsets: boolean): boolean {
  if(includeToolsets)
  {
    if(buildData.hasIncoherentDependenciesIncludingToolsets)
    {
      return buildData.hasIncoherentDependenciesIncludingToolsets;
    }
  }
  else
  { if(buildData.hasIncoherentDependencies)
    {
      return buildData.hasIncoherentDependencies;
    }
  }

  return false;
}


const elementOutStyle = style({
  transform: 'translate(20vw, 0)',
  opacity: 0,
});

const elementInStyle = style({
  transform: 'translate(0, 0)',
  opacity: 1,
});

@Component({
  selector: 'mc-build-graph-table',
  templateUrl: './build-graph-table.component.html',
  styleUrls: ['./build-graph-table.component.scss'],
  animations: [
    trigger("noop", [
      transition(":enter", []),
    ]),
    trigger("insertRemove", [
      transition(":enter", [
        elementOutStyle,
        animate("0.5s ease-out", elementInStyle),
      ]),
      transition(":leave", [
        elementInStyle,
        animate("0.5s ease-in", elementOutStyle),
      ]),
    ]),
  ]
})
export class BuildGraphTableComponent implements OnChanges {

  @Input() public graph?: BuildGraph;
  @Input() public includeToolsets?: boolean;
  @Input() public showAllDependencies?: boolean;
  public sortedBuilds?: BuildData[];
  public locked: boolean = false;
  public focusedBuildId?: number;

  getRepo = getRepo;

  constructor() { }

  public toggleLock() {
    this.locked = !this.locked;
    this.hover(this.focusedBuildId);
  }

  public hover(hoveredBuildId?: number) {
    if (!this.sortedBuilds) {
      return;
    }

    if(!this.locked) {
      this.focusedBuildId = hoveredBuildId;
    }

    const sortedBuilds = this.sortedBuilds;

    function getParents(id: number): number[] {
      return sortedBuilds.filter((b) => b.build.dependencies && b.build.dependencies.find(d => d.buildId === id)).map((b) => b.build.id);
    };

    function isParent(childId: number, parentId: number, recurse: boolean): boolean {
      for (const p of getParents(childId)) {
        if (p === parentId) {
          return true;
        }

        if (recurse && isParent(p, parentId, recurse)) {
          return true;
        }
      }
      return false;
    }

    function isDependent(buildId: number, dependencies: BuildRef[], includeToolsets?: boolean): boolean {
      if(includeToolsets)
      {
        return dependencies.some(d => d.buildId == buildId);
      }
      else
      {
        return dependencies.some(d => d.buildId == buildId && d.isProduct);
      }
    }

    const focusedBuildData = sortedBuilds.find(b => b.build.id == this.focusedBuildId);

    for (const b of this.sortedBuilds) {
      if (!focusedBuildData) {
        b.isDependent = false;
        b.isParent = false;
        b.isAncestor = false;
        b.isSameRepository = false;
        b.isFocused = false;
        b.isLocked = false;
      } else {
        const focusedBuild = focusedBuildData.build;
        b.isDependent = focusedBuild.dependencies && isDependent(b.build.id, focusedBuild.dependencies, this.includeToolsets);
        b.isParent = isParent(focusedBuild.id, b.build.id, false);
        b.isAncestor = !b.isParent && isParent(focusedBuild.id, b.build.id, true);
        b.isSameRepository = b.build.id !== focusedBuild.id && getRepo(b.build) === getRepo(focusedBuild);
        b.isFocused = b.build.id === focusedBuild.id;
        b.isLocked = b.isFocused && this.locked;
      }
      b.state = getState(b);
    }
  }

  ngOnChanges(changes: SimpleChanges) {
    if ('graph' in changes && this.graph) {
      this.sortedBuilds = sortBuilds(this.graph);
    }
  }

  public getBuildId(node: BuildData) {
    return node && node.build && node.build.id;
  }

  public isCoherent(node: BuildData) {
    if (this.includeToolsets) {
      return node.coherent.withAll;
    }
    return node.coherent.withProduct;
  }

  public hasIncoherentDependencies(node: BuildData) {
    if (this.includeToolsets) {
      return node.hasIncoherentDependenciesIncludingToolsets;
    }
    return node.hasIncoherentDependencies;
  }
}
