import { Component, OnInit, OnChanges } from "@angular/core";
import { ActivatedRoute } from "@angular/router";
import { map, shareReplay, switchMap, filter, distinctUntilChanged, tap, combineLatest } from 'rxjs/operators';
import { isAfter, compareAsc, parseISO } from "date-fns";

import { BuildGraph, Build, Subscription } from 'src/maestro-client/models';
import { Observable, of, timer, OperatorFunction } from 'rxjs';
import { BuildStatusService } from 'src/app/services/build-status.service';
import { BuildStatusCompleted } from 'src/app/model/build-status';
import { statefulSwitchMap, StatefulResult, statefulPipe } from 'src/stateful';
import { tapLog } from 'src/helpers';
import { BuildService } from 'src/app/services/build.service';
import { trigger, transition, style, animate } from '@angular/animations';
import { Loading, WrappedError } from 'src/stateful/helpers';
import { MaestroService } from 'src/maestro-client/maestro';

interface AzDevBuildInfo {
  isMostRecent: boolean;
  mostRecentFailureLink?: string;
}

const elementOutStyle = style({
  transform: 'translate(100%, 0)',
});

const elementInStyle = style({
  transform: 'translate(0, 0)',
});

@Component({
  selector: "mc-build",
  templateUrl: "./build.component.html",
  styleUrls: ["./build.component.scss"],
  animations: [
    trigger("toast", [
      transition(":enter", [
        elementOutStyle,
        animate("0.5s ease-out", elementInStyle),
      ]),
      transition(":leave", [
        elementInStyle,
        animate("0.5s ease-in", elementOutStyle),
      ]),
    ]),
  ],
})
export class BuildComponent implements OnInit, OnChanges {
  public constructor(private route: ActivatedRoute, private buildService: BuildService, private buildStatusService: BuildStatusService, private maestroService: MaestroService) { }

  public graph$!: Observable<StatefulResult<BuildGraph>>;
  public build$!: Observable<StatefulResult<Build>>;
  public azDevBuildInfo$!: Observable<StatefulResult<AzDevBuildInfo>>;
  public azDevOnGoingBuildsInfo$!: Observable<StatefulResult<string | null>>;
  public includeToolsets: boolean = false;
  public showAllDependencies: boolean = false;

  public neverToastNewBuilds: boolean = false;

  public toastVisible: boolean = false;
  public toastDate?: Date;
  public acceptToast?: () => void;

  public subscriptionsList$!: Observable<StatefulResult<Subscription[]>>;

  public view$?: Observable<string>;

  private toastNewBuild(): OperatorFunction<number,number> {
    const self = this;
    let haveBuild = false;
    return function(source: Observable<number>) {
      return new Observable<number>(observer => {
        const sourceSub = source.subscribe({
          next(buildId) {
            if (!haveBuild || self.neverToastNewBuilds) {
              haveBuild = true;
              observer.next(buildId);
              return;
            }
            console.log("Toasting Latest Build: ", buildId);
            self.toastVisible = true;
            self.toastDate = new Date();
            self.acceptToast = () => {
              console.log("Accepting Latest Build: ", buildId);
              self.toastVisible = false;
              observer.next(buildId);
            };
          },
          error(err) {
            observer.error(err);
          },
          complete() {
            observer.complete();
          }
        });

        return () => sourceSub.unsubscribe();
      });
    }
  }

  public ngOnInit() {
    const params$ = this.route.paramMap.pipe(
      map(params => {
        const buildId = params.get("buildId");
        const channelId = params.get("channelId");
        const repository = params.get("repository");
        const tabName = params.get("tabName");
        if (buildId == null) {
          throw new Error("buildId was null");
        }
        if (channelId == null) {
          throw new Error("channelId was null");
        }
        if (repository == null) {
          throw new Error("repository was null");
        }
        if (tabName == null) {
          throw new Error("tabName was null");
        }
        return {buildId, channelId, repository, tabName};
      }),
      tap(v => {
        console.log("Params: ", v);
        this.toastVisible = false;
      }),
      shareReplay({
        refCount: true,
        bufferSize: 1,
      }),
    );

    this.view$ = params$.pipe(
      map(params => params.tabName),
    );

    let haveBuildId = false;
    let prevParams: {
      buildId: string;
      channelId: string;
      repository: string;
    } | undefined = undefined;
    const buildId$ = params$.pipe(
      filter(params => {
        if(prevParams) {
          if (prevParams.buildId === params.buildId &&
              prevParams.channelId === params.channelId &&
              prevParams.repository === params.repository) {
            // If the important parameters haven't changed don't reload the build
            return false;
          }
        }

        prevParams = params;
        return true;
      }),
      switchMap(params => {
        if (params.buildId == "latest") {
          return this.buildService.getLatestBuildId(+params.channelId, params.repository).pipe(
            statefulPipe(
              this.toastNewBuild(),
            ),
          );
        }
        else {
          return of(+params.buildId);
        }
      }),
      filter(r => {
        if (!(r instanceof WrappedError)) {
          if (!(r instanceof Loading)) {
            haveBuildId = true;
          }
          return true;
        }
        if (haveBuildId) {
          return false; // ignore errors retrieving latest if we have a build already (TODO: show something ?)
        }
        return true;
      }),
      tapLog("Showing Latest:"),
      shareReplay({
        bufferSize: 1,
        refCount: true,
      }),
    );
    this.build$ = buildId$.pipe(
      statefulPipe(
        switchMap(id => this.buildService.getBuild(id)),
      ),
    );
    this.graph$ = buildId$.pipe(
      statefulPipe(
        statefulSwitchMap((id) => {
          return this.buildService.getBuildGraph(id);
        }),
      ),
    );


    const reloadInterval = 1000 * 60 * 5;
    let emittedLoading = false;
    this.azDevBuildInfo$ = this.build$.pipe(
      statefulPipe(
        switchMap(b => {
          return timer(0, reloadInterval).pipe(
            map(() => b),
          );
        }),
        tap(() => console.log("getting azdev info")),
        statefulSwitchMap(b => this.getBuildInfo(b)),
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
      ),
    );

    this.azDevOnGoingBuildsInfo$ = this.build$.pipe(
      statefulPipe(
        switchMap(b => {
          return timer(0, reloadInterval).pipe(
            map(() => b),
          );
        }),
        tap(() => console.log("getting azdev info")),
        statefulSwitchMap(b => this.getOngoingBuildInfo(b)),
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
      ),
    )

    this.subscriptionsList$ = this.build$.pipe(
      statefulPipe(
        combineLatest(params$),
        statefulSwitchMap(([build, params]) => {
          const currentChannelId = +params.channelId;
          return this.maestroService.subscriptions.listSubscriptionsAsync({
            targetRepository: this.getRepo(build),
          }).pipe(
            map(subs => {
              const result: Record<string, Subscription[]> = {};
              for (let sub of subs) {
                const key = sub.targetBranch || "Unknown Branch";
                if (!result[key]) {
                  result[key] = [];
                }
                result[key].push(sub);
              }
              return result;
            }),
          )
        }),
      )),
      tap(() => console.log("getting subscriptions"));
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
    return this.buildStatusService.getBranchStatus(build.azureDevOpsAccount, build.azureDevOpsProject, build.azureDevOpsBuildDefinitionId, build.azureDevOpsBranch, 5, "completed")
      .pipe(
        map(builds => {
          function isNewer(b: BuildStatusCompleted): boolean {
            if (b.id === build.azureDevOpsBuildId) {
              return false;
            }
            return isAfter(parseISO(b.finishTime!), build.dateProduced);
          }
          if (!build.azureDevOpsAccount) {
            throw new Error("azureDevOpsAccount undefined");
          }

          let isMostRecent: boolean;
          let mostRecentFailureLink: string | undefined;

          const newerBuilds = builds.value.filter(isNewer).sort((l, r) => compareAsc(parseISO(l.finishTime), parseISO(r.finishTime)));
          if (!newerBuilds.length) {
            isMostRecent = true;
            mostRecentFailureLink = undefined;
          } else {
            isMostRecent = false;
            // Yes, it's "canceled".
            const recentFailure = newerBuilds.find(b => (b.result == "failed" || b.result == "canceled") );
            if (recentFailure) {
              mostRecentFailureLink = recentFailure._links.web.href;
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

  public getOngoingBuildInfo(build: Build): Observable<string | null> {
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
    return this.buildStatusService.getBranchStatus(build.azureDevOpsAccount, build.azureDevOpsProject, build.azureDevOpsBuildDefinitionId, build.azureDevOpsBranch, 1, "inProgress")
    .pipe(
      map(builds => {
        if (builds.count > 0) {
          return builds.value[0]._links.web.href;
        }
        return null;
      }),
    );
  }

  public getRepo(build: Build) {
    return build.gitHubRepository || build.azureDevOpsRepository;
  }
}
