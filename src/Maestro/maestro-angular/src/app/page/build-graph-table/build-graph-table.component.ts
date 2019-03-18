import { Component, OnInit, Input, OnChanges } from '@angular/core';
import { Build, BuildGraph } from 'src/maestro-client/models';
import { topologicalSort } from 'src/helpers';

interface BuildData {
  build: Build;
  coherent: boolean;
  isDependent?: boolean;
  isParent?: boolean;
  isAncestor?: boolean;
  isSameRepository?: boolean;
  isFocused?: boolean;
  isLocked?: boolean;
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

function sortBuilds(graph: BuildGraph): BuildData[] {
  const sortedBuilds = topologicalSort(Object.values(graph.builds), build => {
    if (build.dependencies) {
      return build.dependencies.map(dep => graph.builds[dep.buildId]);
    }
    return [];
  }, build => build.id);


  const result = sortedBuilds.map<BuildData>(build => {
      const sameRepo = sortedBuilds.filter(b => getRepo(b) === getRepo(build));
      const coherent = sameRepo.every(b => buildNumber(b) <= buildNumber(build));
      return {
        build: build,
        coherent: coherent,
      };
  });

  return result.reverse();
}

@Component({
  selector: 'mc-build-graph-table',
  templateUrl: './build-graph-table.component.html',
  styleUrls: ['./build-graph-table.component.scss']
})
export class BuildGraphTableComponent implements OnChanges {

  @Input() public graph?: BuildGraph;
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

  ngOnChanges() {
    if (this.graph) {
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
      `?_a=history&buildId=${build.azureDevOpsBuildId}`;
  }
}
