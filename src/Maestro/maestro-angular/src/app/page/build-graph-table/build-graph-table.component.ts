import { Component, OnInit, Input, OnChanges, SimpleChanges } from '@angular/core';
import { Build, BuildGraph } from 'src/maestro-client/models';
import { topologicalSort } from 'src/helpers';
import { trigger, transition, style, animate } from '@angular/animations';

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
}

function getRepo(build: Build) {
  return build.gitHubRepository || build.azureDevOpsRepository;
}

function buildNumber(b : Build): number | string {
  let result: number | string = 0;
  if (b.azureDevOpsBuildNumber) {
    result = +b.azureDevOpsBuildNumber;
    if (isNaN(result)) {
      result = b.azureDevOpsBuildNumber;
    }
  }
  return result;
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
      const coherentWithAll = sameRepo.every(b => buildNumber(b) <= buildNumber(build));
      const coherentWithProducts = sameRepoProducts.every(b => buildNumber(b) <= buildNumber(build));
      return {
        build: build,
        coherent: {
          withAll: coherentWithAll,
          withProduct: coherentWithProducts,
        },
      };
  });

  result = result.reverse();
  for (const node of result) {
    node.isToolset = isToolset(node.build, graph);
  }

  return result;
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
        b.isDependent = focusedBuild.dependencies && focusedBuild.dependencies.some(d => d.buildId == b.build.id);
        b.isParent = isParent(focusedBuild.id, b.build.id, false);
        b.isAncestor = !b.isParent && isParent(focusedBuild.id, b.build.id, true);
        b.isSameRepository = b.build.id !== focusedBuild.id && b.build.gitHubRepository === focusedBuild.gitHubRepository;
        b.isFocused = b.build.id === focusedBuild.id;
        b.isLocked = b.isFocused && this.locked;
      }
    }
  }

  ngOnChanges(changes: SimpleChanges) {
    if ('graph' in changes && this.graph) {
      this.sortedBuilds = sortBuilds(this.graph);
    }
  }

  public getCommitLink(build: Build): string | undefined {
    if (!build ||
        !build.azureDevOpsRepository) {
      return;
    }
    return `${build.azureDevOpsRepository}` +
      `?_a=history&version=GC${build.commit}`;
  }

  public getBuildLink(build: Build): string | undefined {
    if (!build ||
        !build.azureDevOpsAccount ||
        !build.azureDevOpsProject) {
      return;
    }
    return `https://dev.azure.com` +
      `/${build.azureDevOpsAccount}` +
      `/${build.azureDevOpsProject}` +
      `/_build/results` +
      `?view=results&buildId=${build.azureDevOpsBuildId}`;
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
}
