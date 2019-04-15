import { Component, Input, OnChanges } from '@angular/core';
import { BuildGraph, Build } from 'src/maestro-client/models';
import { TreeNode } from 'src/tree-view';
import { compareDesc } from 'date-fns';
import { GetRepositoryNamePipe } from 'src/app/pipes/get-repository-name.pipe';

function toTree(build: Build, builds: Record<string, Build>, includeToolsets: boolean, root: boolean): TreeNode {
  const childBuilds = build.dependencies &&
    build.dependencies.filter(ref => includeToolsets || ref.isProduct)
      .map(ref => builds[ref.buildId]);
  return {
    data: build,
    children: childBuilds && childBuilds.map(b => toTree(b, builds, includeToolsets, false)),
    startOpen: root,
  };
}

@Component({
  selector: 'mc-build-graph-tree',
  templateUrl: './build-graph-tree.component.html',
  styleUrls: ['./build-graph-tree.component.scss'],
})
export class BuildGraphTreeComponent implements OnChanges {

  @Input() public rootId?: number;
  @Input() public graph?: BuildGraph;
  @Input() public includeToolsets?: boolean;

  public tree?: TreeNode[];

  constructor() { }

  ngOnChanges() {
    if (this.graph && this.rootId) {
      const builds: (Build & {coherent?: boolean;})[] = Object.values(this.graph.builds).sort((l, r) => compareDesc(l.dateProduced, r.dateProduced));
      const foundRepos: Record<string, Build> = {};
      for (const build of builds) {
        const repo = GetRepositoryNamePipe.prototype.transform.call(undefined, build);
        if (repo)  {
          if (repo in foundRepos) {
            build.coherent = foundRepos[repo] === build;
          } else {
            foundRepos[repo] = build;
            build.coherent = true;
          }
        }
      }

      const buildMap: Record<string, Build> = {};
      for (const build of builds) {
        buildMap[build.id] = build;
      }

      const rootBuild = buildMap[this.rootId];
      this.tree = [toTree(rootBuild, buildMap, !!this.includeToolsets, true)];
    }
    else {
      this.tree = undefined;
    }
  }

}
