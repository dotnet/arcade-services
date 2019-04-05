import { Component, OnInit, OnChanges } from "@angular/core";
import { ActivatedRoute } from "@angular/router";
import { prettyRepository } from "src/app/util/names";
import { map, shareReplay, switchMap, filter, distinctUntilChanged, tap } from 'rxjs/operators';
import { isAfter, compareAsc, parseISO } from "date-fns";

import { BuildGraph, Build } from 'src/maestro-client/models';
import { MaestroService } from 'src/maestro-client';
import { Observable, of, timer } from 'rxjs';
import { BuildStatusService } from 'src/app/services/build-status.service';
import { BuildStatus } from 'src/app/model/build-status';
import { statefulSwitchMap, StatefulResult, statefulPipe } from 'src/stateful';
import { getCommitLink, getBuildLink } from 'src/helpers';
import { Loading } from 'src/stateful/helpers';

interface AzDevBuildInfo {
  isMostRecent: boolean;
  mostRecentFailureLink?: string;
}

@Component({
  selector: "mc-build",
  templateUrl: "./build.component.html",
  styleUrls: ["./build.component.scss"],
})
export class BuildComponent implements OnInit, OnChanges {
  public repositoryDisplay = prettyRepository;

  public constructor(private route: ActivatedRoute, private maestro: MaestroService, private buildStatusService: BuildStatusService) { }

  public graph$!: Observable<StatefulResult<BuildGraph>>;
  public build$!: Observable<StatefulResult<Build>>;
  public azDevBuildInfo$!: Observable<StatefulResult<AzDevBuildInfo>>;

  public includeToolsets: boolean = false;

  static buildCache: Record<number, Build> = {};
  private getBuild(buildId: number): Observable<StatefulResult<Build>> {
    return of(buildId).pipe(
      statefulSwitchMap(id => {
        if (id in BuildComponent.buildCache) {
          return of(BuildComponent.buildCache[buildId]);
        }
        return this.maestro.builds.getBuildAsync({id}).pipe(
          tap(build => BuildComponent.buildCache[id] = build),
        );
      }),
    );
  }

  // reload the data every 5 minutes
  static reloadInterval = 1000 * 60 * 5;

  private getLatestBuildId(channelId: number, repository: string): Observable<StatefulResult<number>> {
    let emittedLoading = false;
    return timer(0, BuildComponent.reloadInterval).pipe(
      statefulSwitchMap(() => {
        return this.maestro.builds.getLatestAsync({
          channelId,
          repository,
        }).pipe(
          map(build => {
            // dump the build in the cache so we don't hit the server again for it
            BuildComponent.buildCache[build.id] = build;
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
      tap(b => console.log("Latest: ", b)),
    );
  }

  public ngOnInit() {
    const buildId$ = this.route.paramMap.pipe(
      map(params => {
        const buildId = params.get("buildId");
        const channelId = params.get("channelId");
        const repository = params.get("repository");
        if (buildId == null) {
          throw new Error("buildId was null");
        }
        if (channelId == null) {
          throw new Error("channelId was null");
        }
        if (repository == null) {
          throw new Error("repository was null");
        }
        return {buildId, channelId, repository};
      }),
      tap(v => console.log("Params: ", v)),
      switchMap(params => {
        if (params.buildId == "latest") {
          return this.getLatestBuildId(+params.channelId, params.repository);
        }
        else {
          return of(params.buildId);
        }
      }),
      shareReplay(1),
    );
    this.build$ = buildId$.pipe(
      statefulPipe(
        switchMap(id => this.getBuild(id)),
      ),
    );
    this.graph$ = buildId$.pipe(
      statefulPipe(
        statefulSwitchMap((id) => {
          return this.maestro.builds.getBuildGraphAsync({id: id});
        }),
      ),
    );

    this.azDevBuildInfo$ = this.build$.pipe(
      statefulPipe(
        switchMap(b => {
          return timer(0, BuildComponent.reloadInterval).pipe(
            map(() => b),
          );
        }),
        tap(() => console.log("getting azdev info")),
        statefulSwitchMap(b => this.getBuildInfo(b)),
      ),
    );
  }

  public ngOnChanges() {
  }

  public haveAzDevInfo(build: Build): boolean {
    return !!build.azureDevOpsAccount &&
           !!build.azureDevOpsProject &&
           !!build.azureDevOpsBuildDefinitionId &&
           !!build.azureDevOpsBranch;
  }


  public getBuildInfo(build: Build): Observable<AzDevBuildInfo> {
    if (!build.azureDevOpsAccount) {
      throw new Error("azureDevOpsAccount undefined");
    }
    if (!build.azureDevOpsProject) {
      throw new Error("azureDevOpsProject undefined");
    }
    if (!build.azureDevOpsBuildDefinitionId) {
      throw new Error("azureDevOpsBuildDefinitionId undefined");
    }
    if (!build.azureDevOpsBranch) {
      throw new Error("azureDevOpsBranch undefined");
    }
    return this.buildStatusService.getBranchStatus(build.azureDevOpsAccount, build.azureDevOpsProject, build.azureDevOpsBuildDefinitionId, build.azureDevOpsBranch, 5)
      .pipe(
        map(builds => {
          function isNewer(b: BuildStatus): boolean {
            if (b.status === "inProgress") {
              return false;
            }
            if (b.id === build.azureDevOpsBuildId) {
              return false;
            }
            return isAfter(parseISO(b.finishTime), build.dateProduced);
          }

          let isMostRecent: boolean;
          let mostRecentFailureLink: string | undefined;

          const newerBuilds = builds.value.filter(isNewer).sort((l, r) => compareAsc(parseISO(l.finishTime), parseISO(r.finishTime)));
          if (!newerBuilds.length) {
            isMostRecent = true;
            mostRecentFailureLink = undefined;
          } else {
            isMostRecent = false;
            const recentFailure = newerBuilds.find(b => b.result == "failed");
            if (recentFailure) {
              mostRecentFailureLink = this.getBuildLinkFromAzdo(build.azureDevOpsAccount as string, build.azureDevOpsProject as string, recentFailure.id);
            } else {
              mostRecentFailureLink = undefined;
            }
          }

          console.log(`Determined isMostRecent:${isMostRecent}, mostRecentFailureLink:${mostRecentFailureLink}`);
          return {
            isMostRecent,
            mostRecentFailureLink,
          };
        }),
      );
  }

  public getCommitLink = getCommitLink;

  public getBuildLink = getBuildLink;

  public getRepo(build: Build) {
    return build.gitHubRepository || build.azureDevOpsRepository;
  }

  public getBuildLinkFromAzdo(account: string, project: string, buildId: number): string {
    return `https://dev.azure.com` +
      `/${account}` +
      `/${project}` +
      `/_build/results` +
      `?view=results&buildId=${buildId}`;
  }
}
