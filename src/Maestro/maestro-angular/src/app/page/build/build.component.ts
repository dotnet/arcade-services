import { Component, OnInit, OnChanges } from "@angular/core";
import { ActivatedRoute } from "@angular/router";
import { prettyRepository } from "src/app/util/names";
import { map, tap, shareReplay, delay, concat } from 'rxjs/operators';
import moment from 'moment';

import { BuildGraph, Build, BuildRef } from 'src/maestro-client/models';
import { MaestroService } from 'src/maestro-client';
import { Observable, Subject, combineLatest, of } from 'rxjs';
import { BuildStatusService } from 'src/app/services/build-status.service';
import { BuildStatus } from 'src/app/model/build-status';
import { statefulSwitchMap, StatefulResult, statefulPipe } from 'src/stateful';
import { WrappedError, Loading } from 'src/stateful/helpers';

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

  public ngOnInit() {
    const buildId$ = this.route.paramMap.pipe(
      map(params => +(params.get("buildId") as string)),
    );
    this.graph$ = buildId$.pipe(
      statefulSwitchMap(buildId => {
        return this.maestro.builds.getBuildGraphAsync(buildId);
      }),
      shareReplay(1),
    );
    this.build$ = buildId$.pipe(
      statefulSwitchMap(buildId => {
        return this.maestro.builds.getBuildAsync(buildId);
      }),
    );

    this.azDevBuildInfo$ = this.build$.pipe(
      statefulPipe(
        statefulSwitchMap(b => this.getBuildInfo(b)),
      ),
    );
  }

  public ngOnChanges() {
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
            return moment(b.finishTime).isAfter(build.dateProduced);
          }

          let isMostRecent: boolean;
          let mostRecentFailureLink: string | undefined;

          const newerBuilds = builds.value.filter(isNewer).sort((l, r) => moment(l.finishTime).diff(moment(r.finishTime)));
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

  public getCommitLink(build: Build): string {
    if (!build) {
      return "nothing";
    }
    return `https://dev.azure.com/${build.azureDevOpsAccount}` +
      `/${build.azureDevOpsProject}` +
      `/_git` +
      `/${build.azureDevOpsRepository}` +
      `?_a=history&version=GC${build.commit}`;
  }

  public getBuildLink(build: Build): string {
    if (!build) {
      return "nothing";
    }
    return `https://dev.azure.com` +
      `/${build.azureDevOpsAccount}` +
      `/${build.azureDevOpsProject}` +
      `/_build/results` +
      `?view=logs&buildId=${build.azureDevOpsBuildId}`;
  }

  public getBuildLinkFromAzdo(account: string, project: string, buildId: number): string {
    return `https://dev.azure.com` +
      `/${account}` +
      `/${project}` +
      `/_build/results` +
      `?view=logs&buildId=${buildId}`;
  }
}
