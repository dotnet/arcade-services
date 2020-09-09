import { Injectable } from '@angular/core';
import { BuildGraph, Build, Commit } from 'src/maestro-client/models';
import { StatefulResult, statefulSwitchMap } from 'src/stateful';
import { Observable, of, timer } from 'rxjs';
import { MaestroService } from 'src/maestro-client';
import { tap, map, filter, distinctUntilChanged } from 'rxjs/operators';
import { Loading, statefulPipe } from 'src/stateful/helpers';
import { tapLog } from 'src/helpers';

@Injectable({
  providedIn: 'root'
})
export class BuildService {
  static buildCache: Record<number, Build> = {};
  static graphCache: Record<number, BuildGraph> = {};
  static commitsCache: Record<number, Array<Commit>> = {};

  static reloadInterval = 1000 * 60 * 5;
  // static reloadInterval = 1000 * 10;

  constructor(private maestro: MaestroService) { }


  public getBuild(buildId: number): Observable<StatefulResult<Build>> {
    return of(buildId).pipe(
      statefulSwitchMap(id => {
        if (id in BuildService.buildCache) {
          return of(BuildService.buildCache[id]);
        }
        return this.maestro.builds.getBuildAsync({id}).pipe(
          tap(build => BuildService.buildCache[id] = build),
        );
      }),
    );
  }

  public getBuildGraph(buildId: number): Observable<StatefulResult<BuildGraph>> {
    return of(buildId).pipe(
      statefulSwitchMap(id => {
        if (id in BuildService.graphCache) {
          return of(BuildService.graphCache[id]);
        }
        return this.maestro.builds.getBuildGraphAsync({id}).pipe(
          tap(graph => BuildService.graphCache[id] = graph),
        );
      }),
    );
  }

  public getCommits(buildId: number): Observable<StatefulResult<Array<Commit>>> {
    return of(buildId).pipe(
      statefulSwitchMap(id => {
        if (id in BuildService.commitsCache) {
          return of(BuildService.commitsCache[id]);
        }
        console.log("Getting commits")
        return this.maestro.builds.getCommitsAsync({id}).pipe(
          tap(commits => BuildService.commitsCache[id] = commits),
        );
      }),
    );
  }

  public getLatestBuildId(channelId: number, repository: string): Observable<StatefulResult<number>> {
    let emittedLoading = false;
    return timer(0, BuildService.reloadInterval).pipe(
      statefulSwitchMap(() => {
        return this.maestro.builds.getLatestAsync({
          channelId,
          repository,
          loadCollections: true,
        }).pipe(
          map(build => {
            // dump the build in the cache so we don't hit the server again for it
            BuildService.buildCache[build.id] = build;
            return build.id;
          }),
        );
      }),
      filter(r => {
        if (!(r instanceof Loading)) {
          return true;
        }
        // emit only the first "Loading" instance so refreshes don't cause the loading spinner to show up
        if (!emittedLoading)  {
          emittedLoading = true;
          return true;
        }
        return false;
      }),
      statefulPipe(
        distinctUntilChanged(), // don't re-emit the same buildid
      ),
      tapLog(`Got Latest: ${channelId}, ${repository}`),
    );
  }
}
